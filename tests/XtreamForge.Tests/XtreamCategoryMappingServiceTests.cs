using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Data;
using XtreamForge.Categories;

namespace XtreamForge.Tests;

public sealed class XtreamCategoryMappingServiceTests
{
    [Fact]
    public async Task SyncCategoriesAsync_CreatesStableSourceAndDedicatedMappings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var service = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();

        var firstResult = await service.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "Alpha"),
                new DiscoveredCategory("57", "Beta")
            ]);

        var secondResult = await service.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("57", "Beta"),
                new DiscoveredCategory("81", "Gamma"),
                new DiscoveredCategory("42", "Alpha")
            ]);

        Assert.Collection(
            firstResult,
            first => Assert.Equal(("1", "Alpha"), (first.CategoryId, first.CategoryName)),
            second => Assert.Equal(("2", "Beta"), (second.CategoryId, second.CategoryName)));

        Assert.Collection(
            secondResult,
            first => Assert.Equal(("1", "Alpha"), (first.CategoryId, first.CategoryName)),
            second => Assert.Equal(("2", "Beta"), (second.CategoryId, second.CategoryName)),
            third => Assert.Equal(("3", "Gamma"), (third.CategoryId, third.CategoryName)));

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        Assert.Equal(1, await dbContext.XtreamSources.CountAsync());
        Assert.Equal(3, await dbContext.OutputCategories.CountAsync());
        Assert.Equal(0, await dbContext.CustomCategories.CountAsync());
        Assert.Equal(3, await dbContext.UpstreamCategories.CountAsync());
    }

    [Fact]
    public async Task SaveCategoryConfigurationAsync_CanCreateAndReuseGlobalCustomCategoriesAcrossSources()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-a.example", 443), ContentType.Vod, [new DiscoveredCategory("10", "|FR| 4K UHD")]);
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-b.example", 443), ContentType.Vod, [new DiscoveredCategory("500", "|FR| UHD")]);

        var sourceAId = await GetSourceIdAsync(serviceProvider, "source-a.example");
        var sourceBId = await GetSourceIdAsync(serviceProvider, "source-b.example");
        var sourceACategory = await GetUpstreamCategoryAsync(serviceProvider, sourceAId, "10");
        var sourceBCategory = await GetUpstreamCategoryAsync(serviceProvider, sourceBId, "500");

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
            sourceACategory.Id,
            sourceAId,
            ContentType.Vod,
            CategoryMappingSelection.Custom,
            null,
            "Movies 4K"));

        var customCategory = await GetSingleCustomCategoryAsync(serviceProvider, ContentType.Vod);

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
            sourceBCategory.Id,
            sourceBId,
            ContentType.Vod,
            CategoryMappingSelection.Custom,
            customCategory.Id,
            null));

        await using var verifyScope = serviceProvider.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var persistedCategories = await dbContext.UpstreamCategories
            .Where(category => category.ContentType == ContentType.Vod)
            .OrderBy(category => category.XtreamSourceId)
            .ThenBy(category => category.UpstreamCategoryId)
            .ToListAsync();

        Assert.Equal(1, await dbContext.CustomCategories.CountAsync());
        Assert.All(persistedCategories, category => Assert.Equal(customCategory.Id, category.CustomCategoryId));

        var sourceAResult = await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-a.example", 443), ContentType.Vod, [new DiscoveredCategory("10", "|FR| 4K UHD")]);
        var sourceBResult = await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-b.example", 443), ContentType.Vod, [new DiscoveredCategory("500", "|FR| UHD")]);

        Assert.Equal("Movies 4K", Assert.Single(sourceAResult).CategoryName);
        Assert.Equal("Movies 4K", Assert.Single(sourceBResult).CategoryName);
        Assert.Equal(customCategory.XtreamForgeCategoryId.ToString(), Assert.Single(sourceAResult).CategoryId);
        Assert.Equal(customCategory.XtreamForgeCategoryId.ToString(), Assert.Single(sourceBResult).CategoryId);
    }

    [Fact]
    public async Task SaveCategoryConfigurationAsync_DisabledAndOriginalSelectionsPersistWithoutDeletingDiscoveryData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider, "example.com");
        var category = await GetUpstreamCategoryAsync(serviceProvider, sourceId, "42");

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
            category.Id,
            sourceId,
            ContentType.Vod,
            CategoryMappingSelection.Custom,
            null,
            "Movies"));

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
            category.Id,
            sourceId,
            ContentType.Vod,
            CategoryMappingSelection.Disabled,
            null,
            null));

        var disabledView = await mappingService.GetAdministrationViewAsync(sourceId, ContentType.Vod);
        var disabledCategory = Assert.Single(disabledView.UpstreamCategories, item => item.Id == category.Id);
        Assert.True(disabledCategory.IsManuallyExcluded);
        Assert.Equal(CategoryInclusionDecision.Exclude, disabledCategory.EffectiveDecision);
        Assert.Equal(CategoryMappingSelection.Disabled, disabledCategory.CurrentMappingSelection);
        Assert.Null(disabledCategory.CustomCategoryId);

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
            category.Id,
            sourceId,
            ContentType.Vod,
            CategoryMappingSelection.Original,
            null,
            null));

        var restoredCategory = await GetUpstreamCategoryAsync(serviceProvider, sourceId, "42");
        Assert.False(restoredCategory.IsExcluded);
        Assert.Null(restoredCategory.CustomCategoryId);
        Assert.NotEqual(0, restoredCategory.DedicatedOutputCategoryId);
    }

    [Fact]
    public async Task SaveCategoryConfigurationAsync_DisabledSelectionReleasesCustomCategoryUsage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider, "example.com");
        var category = await GetUpstreamCategoryAsync(serviceProvider, sourceId, "42");

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
            category.Id,
            sourceId,
            ContentType.Vod,
            CategoryMappingSelection.Custom,
            null,
            "Movies"));

        var customCategory = await GetSingleCustomCategoryAsync(serviceProvider, ContentType.Vod);

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
            category.Id,
            sourceId,
            ContentType.Vod,
            CategoryMappingSelection.Disabled,
            null,
            null));

        await mappingService.DeleteCustomCategoryAsync(new CustomCategoryDeleteCommand(customCategory.Id, ContentType.Vod));

        await using var verifyScope = serviceProvider.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        Assert.Equal(0, await dbContext.CustomCategories.CountAsync());
    }

    [Fact]
    public async Task GetAdministrationViewAsync_ReturnsMatchedRuleDetailsAndEffectiveStatus()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "|FR| DOCUMENTAIRE SPORT")]);
        var sourceId = await GetSourceIdAsync(serviceProvider, "example.com");

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));

        var view = await mappingService.GetAdministrationViewAsync(sourceId, ContentType.Vod);
        var category = Assert.Single(view.UpstreamCategories);

        Assert.Equal(CategoryInclusionDecision.Include, category.EffectiveDecision);
        Assert.Equal(CategoryInclusionDecision.Include, category.RuleDecision);
        Assert.Equal(10, category.MatchedRuleSequence);
        Assert.Equal(CategoryRuleAction.Include, category.MatchedRuleAction);
        Assert.Equal(CategoryRuleOperator.Contains, category.MatchedRuleOperator);
        Assert.Equal("DOCUMENTAIRE", category.MatchedPattern);
    }

    [Fact]
    public async Task GetAdministrationViewAsync_ReturnsOnlySelectedContentTypeData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("10", "VOD Sport")]);
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Series, [new DiscoveredCategory("20", "Series Drama")]);

        var sourceId = await GetSourceIdAsync(serviceProvider, "example.com");
        var vodCategory = await GetUpstreamCategoryAsync(serviceProvider, sourceId, "10");

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
            vodCategory.Id,
            sourceId,
            ContentType.Vod,
            CategoryMappingSelection.Custom,
            null,
            "Movies 4K"));

        var vodView = await mappingService.GetAdministrationViewAsync(sourceId, ContentType.Vod);
        var seriesView = await mappingService.GetAdministrationViewAsync(sourceId, ContentType.Series);

        Assert.Single(vodView.UpstreamCategories);
        Assert.Equal("VOD Sport", Assert.Single(vodView.UpstreamCategories).UpstreamCategoryName);
        Assert.Single(vodView.CustomCategories);
        Assert.Equal("Movies 4K", Assert.Single(vodView.CustomCategories).DisplayName);

        Assert.Single(seriesView.UpstreamCategories);
        Assert.Equal("Series Drama", Assert.Single(seriesView.UpstreamCategories).UpstreamCategoryName);
        Assert.Empty(seriesView.CustomCategories);
    }

    [Fact]
    public async Task ManualDisabledCategory_StillReportsMatchedRuleWhileManualOverrideTakesPrecedence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "DOCUMENTAIRE")]);
        var sourceId = await GetSourceIdAsync(serviceProvider, "example.com");
        var category = await GetUpstreamCategoryAsync(serviceProvider, sourceId, "42");

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true));
        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Disabled, null, null));

        var view = await mappingService.GetAdministrationViewAsync(sourceId, ContentType.Vod);
        var summary = Assert.Single(view.UpstreamCategories);

        Assert.True(summary.IsManuallyExcluded);
        Assert.Equal(CategoryInclusionDecision.Exclude, summary.EffectiveDecision);
        Assert.Equal(CategoryInclusionDecision.Include, summary.RuleDecision);
        Assert.Equal(10, summary.MatchedRuleSequence);
    }

    [Fact]
    public async Task EffectiveReverseMapping_ExcludesRuleFilteredCategoriesButKeepsGlobalCustomCategoryVisible()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await mappingService.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("10", "Movies A"),
                new DiscoveredCategory("20", "SPORT Movies"),
                new DiscoveredCategory("30", "Movies B")
            ]);

        var sourceId = await GetSourceIdAsync(serviceProvider, "example.com");
        var category20 = await GetUpstreamCategoryAsync(serviceProvider, sourceId, "20");
        var category30 = await GetUpstreamCategoryAsync(serviceProvider, sourceId, "30");

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category20.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies"));
        var customCategory = await GetSingleCustomCategoryAsync(serviceProvider, ContentType.Vod);
        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category30.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Custom, customCategory.Id, null));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));

        var result = await mappingService.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("10", "Movies A"),
                new DiscoveredCategory("20", "SPORT Movies"),
                new DiscoveredCategory("30", "Movies B")
            ]);

        var mappings = await mappingService.GetEffectiveOutputCategoryMappingsAsync(sourceId, ContentType.Vod);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, category => category.CategoryName == "Movies A");
        Assert.Contains(result, category => category.CategoryName == "Movies" && category.IncludedUpstreamCategoryIds.OrderBy(value => value).SequenceEqual(["30"]));
        Assert.Equal(2, mappings.Count);
    }

    [Fact]
    public async Task CustomCategory_RenameAppliesAcrossSources_AndDeleteHonorsUsage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-a.example", 443), ContentType.Vod, [new DiscoveredCategory("10", "One")]);
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-b.example", 443), ContentType.Vod, [new DiscoveredCategory("11", "Two")]);

        var sourceAId = await GetSourceIdAsync(serviceProvider, "source-a.example");
        var sourceBId = await GetSourceIdAsync(serviceProvider, "source-b.example");
        var categoryA = await GetUpstreamCategoryAsync(serviceProvider, sourceAId, "10");
        var categoryB = await GetUpstreamCategoryAsync(serviceProvider, sourceBId, "11");

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(categoryA.Id, sourceAId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Shared"));
        var customCategory = await GetSingleCustomCategoryAsync(serviceProvider, ContentType.Vod);
        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(categoryB.Id, sourceBId, ContentType.Vod, CategoryMappingSelection.Custom, customCategory.Id, null));

        await mappingService.UpdateCustomCategoryAsync(new CustomCategoryUpdateCommand(customCategory.Id, ContentType.Vod, "Renamed Shared"));

        var sourceAResult = await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-a.example", 443), ContentType.Vod, [new DiscoveredCategory("10", "One")]);
        var sourceBResult = await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-b.example", 443), ContentType.Vod, [new DiscoveredCategory("11", "Two")]);
        Assert.Equal("Renamed Shared", Assert.Single(sourceAResult).CategoryName);
        Assert.Equal("Renamed Shared", Assert.Single(sourceBResult).CategoryName);

        var deleteInUse = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mappingService.DeleteCustomCategoryAsync(new CustomCategoryDeleteCommand(customCategory.Id, ContentType.Vod)));
        Assert.Contains("still referenced", deleteInUse.Message, StringComparison.OrdinalIgnoreCase);

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(categoryA.Id, sourceAId, ContentType.Vod, CategoryMappingSelection.Original, null, null));
        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(categoryB.Id, sourceBId, ContentType.Vod, CategoryMappingSelection.Original, null, null));
        await mappingService.DeleteCustomCategoryAsync(new CustomCategoryDeleteCommand(customCategory.Id, ContentType.Vod));

        await using var verifyScope = serviceProvider.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        Assert.Equal(0, await dbContext.CustomCategories.CountAsync());
    }

    [Fact]
    public async Task CreateCustomCategoryAsync_CreatesStandaloneCategory_AndReusesExistingName()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();

        var created = await mappingService.CreateCustomCategoryAsync(new CustomCategoryCreateCommand(ContentType.Vod, "Movies 4K"));
        var reused = await mappingService.CreateCustomCategoryAsync(new CustomCategoryCreateCommand(ContentType.Vod, "  movies 4k  "));

        Assert.Equal(created.Id, reused.Id);
        Assert.Equal("Movies 4K", reused.DisplayName);
        Assert.Equal(0, reused.UsageCount);

        await using var verifyScope = serviceProvider.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        Assert.Equal(1, await dbContext.CustomCategories.CountAsync());
    }

    [Fact]
    public async Task SaveCategoryConfigurationAsync_RejectsCrossContentTypeCustomCategoryMappings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "vod.example", 443), ContentType.Vod, [new DiscoveredCategory("10", "Movies")]);
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "series.example", 443), ContentType.Series, [new DiscoveredCategory("20", "Series")]);

        var vodSourceId = await GetSourceIdAsync(serviceProvider, "vod.example");
        var seriesSourceId = await GetSourceIdAsync(serviceProvider, "series.example");
        var vodCategory = await GetUpstreamCategoryAsync(serviceProvider, vodSourceId, "10");
        var seriesCategory = await GetUpstreamCategoryAsync(serviceProvider, seriesSourceId, "20");

        await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(vodCategory.Id, vodSourceId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies 4K"));
        var customCategory = await GetSingleCustomCategoryAsync(serviceProvider, ContentType.Vod);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(seriesCategory.Id, seriesSourceId, ContentType.Series, CategoryMappingSelection.Custom, customCategory.Id, null)));
    }

    private static ServiceProvider CreateServiceProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CategoryRuleEvaluator());
        services.AddDbContextFactory<XtreamForgeDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<CategoryRuleService>();
        services.AddScoped<XtreamCategoryMappingService>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureCreatedAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static async Task SeedVodCategoriesAsync(XtreamCategoryMappingService mappingService)
    {
        await mappingService.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "|FR| 4K ⁴ᴷ"),
                new DiscoveredCategory("57", "|FR| FILMS 4K UHD"),
                new DiscoveredCategory("94", "|xxx| Something")
            ]);
    }

    private static async Task<int> GetSourceIdAsync(ServiceProvider serviceProvider, string host)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.XtreamSources.Where(source => source.Host == host).Select(source => source.Id).SingleAsync();
    }

    private static async Task<UpstreamCategory> GetUpstreamCategoryAsync(ServiceProvider serviceProvider, int sourceId, string upstreamCategoryId)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.UpstreamCategories.AsNoTracking().SingleAsync(category => category.XtreamSourceId == sourceId && category.UpstreamCategoryId == upstreamCategoryId);
    }

    private static async Task<CustomCategory> GetSingleCustomCategoryAsync(ServiceProvider serviceProvider, ContentType contentType)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.CustomCategories.AsNoTracking().SingleAsync(category => category.ContentType == contentType);
    }
}
