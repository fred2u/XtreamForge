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
        Assert.Equal(0, payload.SourceCount);
        Assert.Equal(0, payload.SourceCategoryCount);
        Assert.Equal(0, payload.RuleCount);
        Assert.Equal(0, payload.CustomCategoryCount);
    }

    [Fact]
    public async Task GetAdminStatus_WithEmptyDatabase_ReturnsZeroCounts()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        using var factory = _factory.WithSqliteDatabase(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/status");
        var payload = await response.Content.ReadFromJsonAsync<AdminStatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("Connected", payload.DatabaseStatus);
        Assert.Equal(0, payload.SourceCount);
        Assert.Equal(0, payload.SourceCategoryCount);
        Assert.Equal(0, payload.RuleCount);
        Assert.Equal(0, payload.CustomCategoryCount);
    }

    [Fact]
    public async Task GetAdminStatus_WithSeededData_ReturnsSummaryCounts()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        await SeedDashboardStatusDataAsync(setupFactory);

        using var factory = _factory.WithSqliteDatabase(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/status");
        var payload = await response.Content.ReadFromJsonAsync<AdminStatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("Connected", payload.DatabaseStatus);
        Assert.Equal(2, payload.SourceCount);
        Assert.Equal(4, payload.SourceCategoryCount);
        Assert.Equal(2, payload.RuleCount);
        Assert.Equal(2, payload.CustomCategoryCount);
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

        var reorderResponse = await client.PutAsJsonAsync(
            "/api/admin/category-rules/order",
            new AdminCategoryRuleOrderRequest(sourceId, "Vod", [secondRuleId, firstRuleId]));
        Assert.Equal(HttpStatusCode.OK, reorderResponse.StatusCode);

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
    public async Task DiscoverSourceEndpoint_ImportsVodAndSeriesCategories_WithoutPersistingCredentials()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        var handler = new FakeForwarderHandler((request, _) => Task.FromResult(FakeForwarderHandler.CreateJsonResponse(
            HttpStatusCode.OK,
            ParseQuery(request.RequestUri, "action") switch
            {
                "get_vod_categories" => """[{"category_id":"10","category_name":"Movies A"}]""",
                "get_series_categories" => """[{"category_id":"20","category_name":"Series A"}]""",
                _ => "[]"
            })));

        using var factory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/sources/discover", new AdminSourceDiscoveryRequest("https", "example.com", null, "user", "pass"));
        var payload = await response.Content.ReadFromJsonAsync<AdminSourceDiscoveryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(1, payload.SourceId);
        Assert.Equal(1, payload.VodCategoryCount);
        Assert.Equal(1, payload.SeriesCategoryCount);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(Assert.IsType<Uri>(request.RequestUri).Query);
            Assert.Equal("user", query["username"]);
            Assert.Equal("pass", query["password"]);
        });
        Assert.Equal("get_vod_categories", ParseQuery(handler.Requests[0].RequestUri, "action"));
        Assert.Equal("get_series_categories", ParseQuery(handler.Requests[1].RequestUri, "action"));

        await using var scope = setupFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var source = await dbContext.XtreamSources.SingleAsync();
        var upstreamCategories = await dbContext.UpstreamCategories.OrderBy(category => category.ContentType).ThenBy(category => category.UpstreamCategoryId).ToListAsync();

        Assert.Equal("https", source.Protocol);
        Assert.Equal("example.com", source.Host);
        Assert.Equal(443, source.Port);
        Assert.Collection(
            upstreamCategories,
            first => Assert.Equal((ContentType.Series, "20", "Series A"), (first.ContentType, first.UpstreamCategoryId, first.UpstreamCategoryName)),
            second => Assert.Equal((ContentType.Vod, "10", "Movies A"), (second.ContentType, second.UpstreamCategoryId, second.UpstreamCategoryName)));
        Assert.DoesNotContain("user", source.Protocol, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user", source.Host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pass", source.Protocol, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pass", source.Host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(upstreamCategories.SelectMany(category => new[] { category.UpstreamCategoryId, category.UpstreamCategoryName }), value => value.Contains("pass", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverSourceEndpoint_ReusesExistingSourceAndPreservesMappings()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        var sourceId = await SeedSingleSourceAsync(setupFactory);
        var upstreamCategoryId = await GetUpstreamCategoryRecordIdAsync(setupFactory, "10");
        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(upstreamCategoryId, sourceId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies Shared"));
        }

        var handler = new FakeForwarderHandler((request, _) => Task.FromResult(FakeForwarderHandler.CreateJsonResponse(
            HttpStatusCode.OK,
            ParseQuery(request.RequestUri, "action") switch
            {
                "get_vod_categories" => """[{"category_id":"10","category_name":"Movies A"},{"category_id":"20","category_name":"Movies B"}]""",
                "get_series_categories" => """[]""",
                _ => "[]"
            })));

        using var factory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/sources/discover", new AdminSourceDiscoveryRequest("https", "https://example.com", null, "user", "pass"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verifyScope = setupFactory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        Assert.Equal(1, await dbContext.XtreamSources.CountAsync());
        var categories = await dbContext.UpstreamCategories.OrderBy(category => category.UpstreamCategoryId).ToListAsync();
        Assert.Equal(2, categories.Count(category => category.ContentType == ContentType.Vod));
        Assert.Equal(1, await dbContext.CustomCategories.CountAsync());
        Assert.Equal(await dbContext.CustomCategories.Select(category => category.Id).SingleAsync(), categories.Single(category => category.UpstreamCategoryId == "10" && category.ContentType == ContentType.Vod).CustomCategoryId);
    }

    [Fact]
    public async Task DiscoverSourceEndpoint_RejectsInvalidInputWithoutLeakingPassword()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/sources/discover", new AdminSourceDiscoveryRequest("https", "not a valid host", 70000, "user", "top-secret"));
        var payload = await response.Content.ReadFromJsonAsync<AdminErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(payload);
        Assert.DoesNotContain("top-secret", payload.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomCategories_UsagesEndpoint_ReturnsMappedSourceCategoriesAcrossSources()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();

            await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "provider-a.com", 443), ContentType.Vod, [new DiscoveredCategory("10", "Movies A")]);
            await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "provider-b.net", 443), ContentType.Vod, [new DiscoveredCategory("20", "Movies B")]);
            await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "provider-a.com", 443), ContentType.Series, [new DiscoveredCategory("30", "Series A")]);

            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var sourceACategory = await dbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "10" && category.ContentType == ContentType.Vod);
            var sourceBCategory = await dbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "20" && category.ContentType == ContentType.Vod);
            var seriesCategory = await dbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "30" && category.ContentType == ContentType.Series);

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(sourceACategory.Id, sourceACategory.XtreamSourceId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies Shared"));
            var customCategoryId = await dbContext.CustomCategories.Where(category => category.ContentType == ContentType.Vod).Select(category => category.Id).SingleAsync();
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(sourceBCategory.Id, sourceBCategory.XtreamSourceId, ContentType.Vod, CategoryMappingSelection.Custom, customCategoryId, null));
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(seriesCategory.Id, seriesCategory.XtreamSourceId, ContentType.Series, CategoryMappingSelection.Custom, null, "Series Shared"));
        }

        await using var verifyScope = setupFactory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var targetCustomCategoryId = await verifyContext.CustomCategories.Where(category => category.ContentType == ContentType.Vod).Select(category => category.Id).SingleAsync();

        using var factory = _factory.WithSqliteDatabase(databasePath);
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/admin/custom-categories/{targetCustomCategoryId}/usages?contentType=Vod");
        var payload = await response.Content.ReadFromJsonAsync<List<AdminCustomCategoryUsageResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Collection(
            payload.OrderBy(item => item.SourceHost).ThenBy(item => item.UpstreamCategoryId),
            first =>
            {
                Assert.True(first.UpstreamCategoryRecordId > 0);
                Assert.Equal(("provider-a.com", 443, "Vod", "10", "Movies A"), (first.SourceHost, first.SourcePort, first.ContentType, first.UpstreamCategoryId, first.UpstreamCategoryName));
                Assert.DoesNotContain("user", first.SourceHost, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("pass", first.SourceHost, StringComparison.OrdinalIgnoreCase);
            },
            second =>
            {
                Assert.True(second.UpstreamCategoryRecordId > 0);
                Assert.Equal(("provider-b.net", 443, "Vod", "20", "Movies B"), (second.SourceHost, second.SourcePort, second.ContentType, second.UpstreamCategoryId, second.UpstreamCategoryName));
                Assert.DoesNotContain("user", second.SourceHost, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("pass", second.SourceHost, StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public async Task UpdateCategoryMapping_ToOriginal_UnlinksCustomCategoryUsageWithoutDeletingCategory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-admin-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        int customCategoryId;
        int sourceCategoryToUnlinkId;
        int sourceId;

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();

            await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "provider-a.com", 443), ContentType.Vod, [new DiscoveredCategory("10", "Movies A")]);
            await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "provider-b.net", 443), ContentType.Vod, [new DiscoveredCategory("20", "Movies B")]);

            var sourceACategory = await dbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "10");
            var sourceBCategory = await dbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "20");
            sourceId = sourceACategory.XtreamSourceId;

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(sourceACategory.Id, sourceACategory.XtreamSourceId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies Shared"));
            customCategoryId = await dbContext.CustomCategories.Where(category => category.ContentType == ContentType.Vod).Select(category => category.Id).SingleAsync();
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(sourceBCategory.Id, sourceBCategory.XtreamSourceId, ContentType.Vod, CategoryMappingSelection.Custom, customCategoryId, null));
            sourceCategoryToUnlinkId = sourceACategory.Id;
        }

        using var factory = _factory.WithSqliteDatabase(databasePath);
        using var client = factory.CreateClient();

        var unlinkResponse = await client.PutAsJsonAsync(
            $"/api/admin/categories/{sourceCategoryToUnlinkId}/mapping",
            new AdminCategoryMappingRequest(sourceId, "Vod", "Original", null, null));

        Assert.Equal(HttpStatusCode.OK, unlinkResponse.StatusCode);

        var usagesResponse = await client.GetAsync($"/api/admin/custom-categories/{customCategoryId}/usages?contentType=Vod");
        var usagesPayload = await usagesResponse.Content.ReadFromJsonAsync<List<AdminCustomCategoryUsageResponse>>();
        Assert.Equal(HttpStatusCode.OK, usagesResponse.StatusCode);
        Assert.NotNull(usagesPayload);
        var remainingUsage = Assert.Single(usagesPayload);
        Assert.Equal("20", remainingUsage.UpstreamCategoryId);

        var categoriesResponse = await client.GetAsync($"/api/admin/categories?sourceId={sourceId}&contentType=Vod");
        var categoriesPayload = await categoriesResponse.Content.ReadFromJsonAsync<AdminCategoriesResponse>();
        Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
        Assert.NotNull(categoriesPayload);
        Assert.Equal(1, Assert.Single(categoriesPayload.CustomCategories).UsageCount);
        Assert.Equal("Original", Assert.Single(categoriesPayload.UpstreamCategories, category => category.UpstreamCategoryId == "10").CurrentMappingSelection);

        await using var verifyScope = setupFactory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var unlinkedCategory = await verifyContext.UpstreamCategories.AsNoTracking().SingleAsync(category => category.Id == sourceCategoryToUnlinkId);
        var otherCategory = await verifyContext.UpstreamCategories.AsNoTracking().SingleAsync(category => category.UpstreamCategoryId == "20");
        var persistedCustomCategory = await verifyContext.CustomCategories.AsNoTracking().SingleAsync(category => category.Id == customCategoryId);

        Assert.False(unlinkedCategory.IsExcluded);
        Assert.Null(unlinkedCategory.CustomCategoryId);
        Assert.Equal(customCategoryId, otherCategory.CustomCategoryId);
        Assert.Equal("Movies Shared", persistedCustomCategory.DisplayName);
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

    private static async Task SeedDashboardStatusDataAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();

        await mappingService.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "provider-a.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("10", "Movies A"),
                new DiscoveredCategory("20", "Sports A")
            ]);

        await mappingService.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "provider-b.net", 8080),
            ContentType.Vod,
            [
                new DiscoveredCategory("30", "Movies B"),
                new DiscoveredCategory("40", "Documentaries")
            ]);

        var sources = await dbContext.XtreamSources.OrderBy(source => source.Host).ToListAsync();
        var sourceAId = sources[0].Id;
        var sourceBId = sources[1].Id;

        var moviesA = await dbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "10");
        var sportsA = await dbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "20");
        var moviesB = await dbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "30");

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(moviesA.Id, sourceAId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies Shared"));
        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(moviesB.Id, sourceBId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Documentaries Shared"));
        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(sportsA.Id, sourceAId, ContentType.Vod, CategoryMappingSelection.Disabled, null, null));

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceAId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceBId, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.StartsWith, "DOC", false, true));
    }

    private static string? ParseQuery(Uri? requestUri, string key)
    {
        if (requestUri is null)
        {
            return null;
        }

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(requestUri.Query);
        return query.TryGetValue(key, out var values) ? values[0] : null;
    }
}
