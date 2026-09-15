using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Admin;
using XtreamForge.Categories;
using XtreamForge.Data;

namespace XtreamForge.Tests;

public sealed class AdminEndpointTests : IClassFixture<XtreamForgeApiFactory>
{
    private readonly XtreamForgeApiFactory _factory;

    public AdminEndpointTests(XtreamForgeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAdminStatus_WhenDatabaseCheckThrows_ReturnsUnavailableFallback()
    {
        using var factory = _factory.WithFailingDatabaseFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/status");
        var payload = await response.Content.ReadFromJsonAsync<AdminStatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("XtreamForge", payload.ApplicationName);
        Assert.Equal("Unavailable", payload.DatabaseStatus);
        Assert.Equal("The database connectivity check failed.", payload.DatabaseDetails);
    }

    [Fact]
    public async Task GetAdminCategories_ReturnsSourcesCategoriesAndCustomCategories()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        var sourceId = await SeedCategoryAdministrationDataAsync(setupFactory);

        using var factory = _factory.WithSqliteDatabase(databasePath);
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/admin/categories?sourceId={sourceId}&contentType=Vod");
        var payload = await response.Content.ReadFromJsonAsync<AdminCategoriesResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(sourceId, payload.SelectedSourceId);
        Assert.Equal("Vod", payload.SelectedContentType);
        Assert.Single(payload.Sources);
        Assert.Single(payload.CustomCategories);
        Assert.Equal("Movies B Shared", payload.CustomCategories[0].DisplayName);

        var disabledCategory = Assert.Single(payload.UpstreamCategories, category => category.UpstreamCategoryId == "20");
        Assert.True(disabledCategory.IsManuallyExcluded);
        Assert.Equal("Disabled", disabledCategory.CurrentMappingSelection);
        Assert.Equal("Exclude", disabledCategory.EffectiveDecision);

        var customCategory = Assert.Single(payload.UpstreamCategories, category => category.UpstreamCategoryId == "30");
        Assert.Equal("Custom", customCategory.CurrentMappingSelection);
        Assert.Equal(payload.CustomCategories[0].Id, customCategory.CustomCategoryId);
        Assert.Equal("Movies B Shared", customCategory.CustomCategoryName);
    }

    [Fact]
    public async Task UpdateCategoryMapping_WithNewCustomCategory_CreatesCategoryAndMapping()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        var sourceId = await SeedSingleSourceAsync(setupFactory);
        var upstreamCategoryId = await GetUpstreamCategoryRecordIdAsync(setupFactory, "10");

        using var factory = _factory.WithSqliteDatabase(databasePath);
        using var client = factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/admin/categories/{upstreamCategoryId}/mapping",
            new AdminCategoryMappingRequest(sourceId, "Vod", "Custom", null, "Movies 4K"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = setupFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var upstreamCategory = await dbContext.UpstreamCategories.Include(category => category.CustomCategory).SingleAsync(category => category.Id == upstreamCategoryId);

        Assert.False(upstreamCategory.IsExcluded);
        Assert.NotNull(upstreamCategory.CustomCategory);
        Assert.Equal("Movies 4K", upstreamCategory.CustomCategory.DisplayName);
    }

    [Fact]
    public async Task CategoryRules_Endpoints_CreateUpdateMoveAndDeleteRules()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        var sourceId = await SeedSingleSourceAsync(setupFactory);

        using var factory = _factory.WithSqliteDatabase(databasePath);
        using var client = factory.CreateClient();

        var createFirst = await client.PostAsJsonAsync("/api/admin/category-rules", new AdminCategoryRuleRequest(sourceId, "Vod", "Exclude", "Contains", "SPORT", false, true));
        var createSecond = await client.PostAsJsonAsync("/api/admin/category-rules", new AdminCategoryRuleRequest(sourceId, "Vod", "Include", "StartsWith", "|FR|", false, true));
        Assert.Equal(HttpStatusCode.OK, createFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, createSecond.StatusCode);

        var rulesResponse = await client.GetFromJsonAsync<AdminCategoryRulesResponse>($"/api/admin/category-rules?sourceId={sourceId}&contentType=Vod");
        Assert.NotNull(rulesResponse);
        Assert.Equal(2, rulesResponse.Rules.Count);
        var firstRuleId = rulesResponse.Rules[0].Id;
        var secondRuleId = rulesResponse.Rules[1].Id;

        var updateResponse = await client.PutAsJsonAsync($"/api/admin/category-rules/{firstRuleId}", new AdminCategoryRuleRequest(sourceId, "Vod", "Exclude", "Contains", "SPORT HD", true, false));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var moveResponse = await client.PostAsync($"/api/admin/category-rules/{firstRuleId}/move-down?sourceId={sourceId}&contentType=Vod", null);
        Assert.Equal(HttpStatusCode.OK, moveResponse.StatusCode);

        var movedRules = await client.GetFromJsonAsync<AdminCategoryRulesResponse>($"/api/admin/category-rules?sourceId={sourceId}&contentType=Vod");
        Assert.NotNull(movedRules);
        Assert.Equal(new[] { secondRuleId, firstRuleId }, movedRules.Rules.Select(rule => rule.Id).ToArray());
        var updatedRule = movedRules.Rules.Single(rule => rule.Id == firstRuleId);
        Assert.Equal("SPORT HD", updatedRule.Pattern);
        Assert.True(updatedRule.CaseSensitive);
        Assert.False(updatedRule.IsEnabled);

        var deleteResponse = await client.DeleteAsync($"/api/admin/category-rules/{secondRuleId}?sourceId={sourceId}&contentType=Vod&confirmDelete=true");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var finalRules = await client.GetFromJsonAsync<AdminCategoryRulesResponse>($"/api/admin/category-rules?sourceId={sourceId}&contentType=Vod");
        Assert.NotNull(finalRules);
        var remainingRule = Assert.Single(finalRules.Rules);
        Assert.Equal(firstRuleId, remainingRule.Id);
        Assert.Equal(10, remainingRule.Sequence);
    }

    [Fact]
    public async Task CategoryRules_PreviewEndpoint_UsesBackendRuleSemantics()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        var sourceId = await SeedCategoryAdministrationDataAsync(setupFactory);

        using var factory = _factory.WithSqliteDatabase(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/admin/category-rules/preview?sourceId={sourceId}&contentType=Vod&categoryName=%7CFR%7C%20DOCUMENTAIRE%20SPORT");
        var payload = await response.Content.ReadFromJsonAsync<AdminCategoryRulePreviewResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("Exclude", payload.Decision);
        Assert.Equal(10, payload.MatchedRuleSequence);
        Assert.Equal("Exclude", payload.MatchedRuleAction);
        Assert.Equal("Contains", payload.MatchedRuleOperator);
        Assert.Equal("SPORT", payload.MatchedPattern);
    }

    [Fact]
    public async Task CustomCategories_Endpoints_CreateRenameAndDeleteUnusedCategory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        using var factory = _factory.WithSqliteDatabase(databasePath);
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/admin/custom-categories", new AdminCustomCategoryCreateRequest("Vod", "Movies 4K"));
        var createdCategory = await createResponse.Content.ReadFromJsonAsync<AdminCustomCategoryResponse>();

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.NotNull(createdCategory);

        var updateResponse = await client.PutAsJsonAsync($"/api/admin/custom-categories/{createdCategory.Id}", new AdminCustomCategoryUpdateRequest("Vod", "Movies Ultra HD"));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/admin/custom-categories/{createdCategory.Id}?contentType=Vod");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        await using var scope = setupFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        Assert.False(await dbContext.CustomCategories.AnyAsync());
    }

    [Fact]
    public async Task RefreshCategoriesEndpoint_ReusesConfiguredXtreamSourceAndPersistsDiscoveredCategories()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        var sourceId = await SeedSingleSourceAsync(setupFactory);
        var handler = new FakeForwarderHandler((_, _) => Task.FromResult(FakeForwarderHandler.CreateJsonResponse(
            HttpStatusCode.OK,
            """[{"category_id":"10","category_name":"Movies A"},{"category_id":"20","category_name":"Movies B"}]""")));

        using var factory = _factory
            .WithSqliteDatabase(databasePath)
            .WithForwarderHandler(handler)
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                {
                    configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Xtream:BaseUrl"] = "https://example.com",
                        ["Xtream:Username"] = "user",
                        ["Xtream:Password"] = "pass"
                    });
                });
            });

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/categories/refresh", new AdminCategoryRefreshRequest(sourceId, "Vod"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(handler.Requests);
        var requestUri = Assert.IsType<Uri>(handler.Requests[0].RequestUri);
        Assert.Equal("/player_api.php", requestUri.AbsolutePath);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(requestUri.Query);
        Assert.Equal("user", query["username"]);
        Assert.Equal("pass", query["password"]);
        Assert.Equal("get_vod_categories", query["action"]);

        await using var scope = setupFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        Assert.Equal(2, await dbContext.UpstreamCategories.CountAsync());
        Assert.Contains(await dbContext.UpstreamCategories.Select(category => category.UpstreamCategoryName).ToListAsync(), name => name == "Movies B");
    }

    private static async Task EnsureDatabaseCreatedAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static async Task<int> SeedCategoryAdministrationDataAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();

        await mappingService.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("10", "Movies A"),
                new DiscoveredCategory("20", "SPORT Movies"),
                new DiscoveredCategory("30", "Movies B")
            ]);

        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var sourceId = await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
        var categories = await dbContext.UpstreamCategories.OrderBy(category => category.UpstreamCategoryId).ToListAsync();
        var category20 = categories.Single(category => category.UpstreamCategoryId == "20");
        var category30 = categories.Single(category => category.UpstreamCategoryId == "30");

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category20.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Disabled, null, null));
        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category30.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies B Shared"));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));

        return sourceId;
    }

    private static async Task<int> SeedSingleSourceAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();

        await mappingService.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [new DiscoveredCategory("10", "Movies A")]);

        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
    }

    private static async Task<int> GetUpstreamCategoryRecordIdAsync(WebApplicationFactory<Program> factory, string upstreamCategoryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.UpstreamCategories.Where(category => category.UpstreamCategoryId == upstreamCategoryId).Select(category => category.Id).SingleAsync();
    }
}
