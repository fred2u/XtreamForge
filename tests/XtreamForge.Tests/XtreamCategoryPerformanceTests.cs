using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Categories;
using XtreamForge.Data;

namespace XtreamForge.Tests;

public sealed class XtreamCategoryPerformanceTests : IClassFixture<XtreamForgeApiFactory>
{
    private readonly XtreamForgeApiFactory _factory;

    public XtreamCategoryPerformanceTests(XtreamForgeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetVodCategories_WarmRepresentativeRequest_PreservesOutputAndAvoidsNPlusOneDatabaseAccess()
    {
        var commandCounter = new DbCommandCounterInterceptor();
        var saveChangesCounter = new SaveChangesCounterInterceptor();
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-performance-tests-{Guid.NewGuid():N}.db");
        var discoveredCategories = CreateRepresentativeCategories();
        var expectedPayload = CreateExpectedRepresentativePayload();
        var upstreamPayload = CreateCategoryPayload(discoveredCategories);

        using var setupFactory = _factory.WithSqliteDatabase(databasePath, commandCounter, saveChangesCounter);
        await EnsureDatabaseCreatedAsync(setupFactory);
        await SeedRepresentativeVodCategoriesAsync(setupFactory, upstreamPayload);

        commandCounter.Reset();
        saveChangesCounter.Reset();

        using var rewriteFactory = _factory.WithSqliteDatabase(databasePath, commandCounter, saveChangesCounter)
            .WithForwarderHandler(CreateJsonHandler(upstreamPayload));
        using var client = rewriteFactory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
        var payload = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(expectedPayload, payload);
        Assert.InRange(commandCounter.CommandCount, 1, 5);
        Assert.InRange(commandCounter.WriteCommandCount, 0, 1);
        Assert.InRange(saveChangesCounter.SaveChangesCount, 0, 1);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(200)]
    [InlineData(1000)]
    public async Task SyncCategoriesAsync_WarmRequests_KeepDatabaseCommandsNearlyConstantAtScale(int categoryCount)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var commandCounter = new DbCommandCounterInterceptor();
        var saveChangesCounter = new SaveChangesCounterInterceptor();
        await using var serviceProvider = CreateServiceProvider(connection, commandCounter, saveChangesCounter);
        await EnsureCreatedAsync(serviceProvider);

        var service = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var categories = Enumerable.Range(1, categoryCount)
            .Select(index => new DiscoveredCategory(index.ToString(), $"Category {index:0000}"))
            .ToList();

        await service.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            categories);

        commandCounter.Reset();
        saveChangesCounter.Reset();

        var result = await service.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            categories);

        Assert.Equal(categoryCount, result.Count);
        Assert.InRange(commandCounter.CommandCount, 1, 4);
        Assert.InRange(commandCounter.WriteCommandCount, 0, 1);
        Assert.InRange(saveChangesCounter.SaveChangesCount, 0, 1);
    }

    private static async Task SeedRepresentativeVodCategoriesAsync(WebApplicationFactory<Program> factory, string upstreamPayload)
    {
        using (var discoveryFactory = factory.WithForwarderHandler(CreateJsonHandler(upstreamPayload)))
        using (var discoveryClient = discoveryFactory.CreateClient())
        {
            var discoveryResponse = await discoveryClient.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
            Assert.Equal(HttpStatusCode.OK, discoveryResponse.StatusCode);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();
        var sourceId = await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
        var upstreamCategories = await dbContext.UpstreamCategories
            .Where(category => category.XtreamSourceId == sourceId && category.ContentType == ContentType.Vod)
            .OrderBy(category => category.UpstreamCategoryId)
            .ToDictionaryAsync(category => category.UpstreamCategoryId);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.StartsWith, "SPORT ", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.StartsWith, "XXX ", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.StartsWith, "IGNORE ", false, true));

        for (var index = 0; index < 7; index++)
        {
            var firstCategoryId = (189 + (index * 2)).ToString();
            var secondCategoryId = (190 + (index * 2)).ToString();
            var customCategoryName = $"Merged Output {index + 1:00}";

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
                upstreamCategories[firstCategoryId].Id,
                sourceId,
                ContentType.Vod,
                CategoryMappingSelection.Custom,
                null,
                customCategoryName));

            var customCategoryId = await dbContext.CustomCategories
                .Where(category => category.ContentType == ContentType.Vod && category.DisplayName == customCategoryName)
                .Select(category => category.Id)
                .SingleAsync();

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
                upstreamCategories[secondCategoryId].Id,
                sourceId,
                ContentType.Vod,
                CategoryMappingSelection.Custom,
                customCategoryId,
                null));
        }
    }

    private static List<DiscoveredCategory> CreateRepresentativeCategories()
    {
        var categories = new List<DiscoveredCategory>(202);

        for (var index = 1; index <= 164; index++)
        {
            var prefix = index % 3 switch
            {
                1 => "SPORT",
                2 => "XXX",
                _ => "IGNORE"
            };

            categories.Add(new DiscoveredCategory(index.ToString(), $"{prefix} Excluded {index:000}"));
        }

        for (var index = 1; index <= 24; index++)
        {
            categories.Add(new DiscoveredCategory((164 + index).ToString(), $"Original {index:00}"));
        }

        for (var index = 1; index <= 7; index++)
        {
            var baseId = 188 + ((index - 1) * 2);
            categories.Add(new DiscoveredCategory((baseId + 1).ToString(), $"Merged {index:00} A"));
            categories.Add(new DiscoveredCategory((baseId + 2).ToString(), $"Merged {index:00} B"));
        }

        return categories;
    }

    private static List<CategoryResponse> CreateExpectedRepresentativePayload()
    {
        var payload = new List<CategoryResponse>(31);

        for (var index = 1; index <= 24; index++)
        {
            payload.Add(new CategoryResponse((164 + index).ToString(), $"Original {index:00}"));
        }

        for (var index = 1; index <= 7; index++)
        {
            payload.Add(new CategoryResponse((202 + index).ToString(), $"Merged Output {index:00}"));
        }

        return payload;
    }

    private static string CreateCategoryPayload(IReadOnlyList<DiscoveredCategory> categories) =>
        JsonSerializer.Serialize(
            categories.Select(category => new Dictionary<string, string>
            {
                ["category_id"] = category.UpstreamCategoryId,
                ["category_name"] = category.UpstreamCategoryName
            }));

    private static FakeForwarderHandler CreateJsonHandler(string json) =>
        new((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        });

    private static ServiceProvider CreateServiceProvider(
        SqliteConnection connection,
        DbCommandCounterInterceptor commandCounter,
        SaveChangesCounterInterceptor saveChangesCounter)
    {
        var services = new ServiceCollection();
        services.AddSingleton<CategoryRuleEvaluator>();
        services.AddDbContextFactory<XtreamForgeDbContext>(options =>
            options.UseSqlite(connection).AddInterceptors(commandCounter, saveChangesCounter));
        services.AddScoped(static serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<XtreamForgeDbContext>>().CreateDbContext());
        services.AddScoped<XtreamCategoryMappingService>();

        return services.BuildServiceProvider();
    }

    private static async Task EnsureCreatedAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static async Task EnsureDatabaseCreatedAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    private sealed record CategoryResponse(
        [property: JsonPropertyName("category_id")] string CategoryId,
        [property: JsonPropertyName("category_name")] string CategoryName);
}
