using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Infrastructure.Data;
using XtreamForge.Infrastructure.Models;
using XtreamForge.Infrastructure.Services;

namespace XtreamForge.Infrastructure.Tests;

public sealed class XtreamCategoryMappingServiceTests
{
    [Fact]
    public async Task SyncCategoriesAsync_CreatesStableSourceAndOutputMappings()
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
        Assert.Equal(3, await dbContext.UpstreamCategories.CountAsync());
    }

    [Fact]
    public async Task SaveCategoryConfigurationAsync_SupportsRenameMergeAndExclude()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        await SeedVodCategoriesAsync(mappingService);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var categories = await dbContext.UpstreamCategories.OrderBy(category => category.UpstreamCategoryId).ToListAsync();
            var category42 = categories.Single(category => category.UpstreamCategoryId == "42");
            var category57 = categories.Single(category => category.UpstreamCategoryId == "57");
            var category94 = categories.Single(category => category.UpstreamCategoryId == "94");

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category42.Id, category42.XtreamSourceId, ContentType.Vod, false, null, "|FR| FILMS 4K"));
            category42 = await dbContext.UpstreamCategories.AsNoTracking().SingleAsync(category => category.Id == category42.Id);
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category57.Id, category57.XtreamSourceId, ContentType.Vod, false, category42.DedicatedOutputCategoryId, "|FR| FILMS 4K"));
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category94.Id, category94.XtreamSourceId, ContentType.Vod, true, null, null));
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category57.Id, category57.XtreamSourceId, ContentType.Vod, false, category42.DedicatedOutputCategoryId, null));
        }

        var result = await mappingService.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "|FR| 4K ⁴ᴷ"),
                new DiscoveredCategory("57", "|FR| FILMS 4K UHD"),
                new DiscoveredCategory("94", "|xxx| Something")
            ]);

        Assert.Single(result);
        Assert.Equal(("1", "|FR| FILMS 4K"), (result[0].CategoryId, result[0].CategoryName));
    }

    [Fact]
    public async Task CreateRuleAsync_RejectsEmptyOrWhitespacePatterns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await Assert.ThrowsAsync<ArgumentException>(() => ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, string.Empty, false, true)));
        await Assert.ThrowsAsync<ArgumentException>(() => ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "   ", false, true)));
    }

    [Fact]
    public async Task Rules_AreScopedBySourceAndContentType()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();

        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-a.example", 443), ContentType.Vod, [new DiscoveredCategory("10", "SPORT")]);
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-a.example", 443), ContentType.Series, [new DiscoveredCategory("20", "SPORT")]);
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-b.example", 443), ContentType.Vod, [new DiscoveredCategory("30", "SPORT")]);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var sourceA = await dbContext.XtreamSources.SingleAsync(source => source.Host == "source-a.example");
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceA.Id, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        var sourceAVod = await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-a.example", 443), ContentType.Vod, [new DiscoveredCategory("10", "SPORT")]);
        var sourceASeries = await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-a.example", 443), ContentType.Series, [new DiscoveredCategory("20", "SPORT")]);
        var sourceBVod = await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "source-b.example", 443), ContentType.Vod, [new DiscoveredCategory("30", "SPORT")]);

        Assert.Empty(sourceAVod);
        Assert.Single(sourceASeries);
        Assert.Single(sourceBVod);
    }

    [Fact]
    public async Task RuleChanges_AffectNextEvaluation_AndExcludedCategoriesRemainPersisted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "SPORT")]);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        Assert.Empty(await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "SPORT") ]));

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            Assert.Equal(1, await dbContext.UpstreamCategories.CountAsync());
            var rule = await dbContext.CategoryRules.SingleAsync();
            rule.Pattern = "MOVIES";
            rule.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        Assert.Single(await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "SPORT") ]));

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var rule = await dbContext.CategoryRules.SingleAsync();
            rule.Pattern = "sport";
            rule.CaseSensitive = true;
            rule.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        Assert.Single(await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "SPORT") ]));

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var rule = await dbContext.CategoryRules.SingleAsync();
            rule.IsEnabled = false;
            rule.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        Assert.Single(await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "SPORT") ]));

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            dbContext.CategoryRules.Remove(await dbContext.CategoryRules.SingleAsync());
            await dbContext.SaveChangesAsync();
        }

        Assert.Single(await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "SPORT") ]));
    }

    [Fact]
    public async Task MoveRuleAsync_PersistsDeterministicOrderingWithoutDuplicateSequences()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.StartsWith, "|XXX|", false, true));

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var includeRule = await dbContext.CategoryRules.SingleAsync(rule => rule.Pattern == "DOCUMENTAIRE");
            await ruleService.MoveRuleAsync(new CategoryRuleIdentityCommand(includeRule.Id, sourceId, ContentType.Vod), CategoryRuleMoveDirection.Up);
        }

        var preview = await ruleService.PreviewAsync(sourceId, ContentType.Vod, "|FR| DOCUMENTAIRE SPORT");
        Assert.NotNull(preview);
        Assert.Equal(CategoryInclusionDecision.Include, preview.Evaluation.Decision);
        Assert.Equal(10, preview.Evaluation.MatchedRuleSequence);

        await using var verifyScope = serviceProvider.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var sequences = await verifyDbContext.CategoryRules
            .Where(rule => rule.XtreamSourceId == sourceId && rule.ContentType == ContentType.Vod)
            .OrderBy(rule => rule.Sequence)
            .Select(rule => rule.Sequence)
            .ToListAsync();

        Assert.Equal([10, 20, 30], sequences);
        Assert.Equal(sequences.Count, sequences.Distinct().Count());
    }

    [Fact]
    public async Task ManualExclusion_TakesPrecedenceOverIncludeRule_AndDoesNotDestroyMapping()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "DOCUMENTAIRE")]);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true));

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var category = await dbContext.UpstreamCategories.SingleAsync();
            var originalOutputCategoryId = category.OutputCategoryId;
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category.Id, sourceId, ContentType.Vod, true, null, null));
            category = await dbContext.UpstreamCategories.AsNoTracking().SingleAsync();
            Assert.Equal(originalOutputCategoryId, category.DedicatedOutputCategoryId);
        }

        var result = await mappingService.SyncCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "DOCUMENTAIRE")]);
        Assert.Empty(result);

        await using var verifyScope = serviceProvider.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var persistedCategory = await verifyDbContext.UpstreamCategories.SingleAsync();
        Assert.True(persistedCategory.IsExcluded);
        Assert.NotEqual(0, persistedCategory.DedicatedOutputCategoryId);
    }

    [Fact]
    public async Task EffectiveReverseMapping_ExcludesRuleFilteredUpstreamCategoriesButKeepsMergedOutputVisible()
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

        var sourceId = await GetSourceIdAsync(serviceProvider);
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var categories = await dbContext.UpstreamCategories.OrderBy(category => category.UpstreamCategoryId).ToListAsync();
            var category10 = categories.Single(category => category.UpstreamCategoryId == "10");
            var category20 = categories.Single(category => category.UpstreamCategoryId == "20");
            var category30 = categories.Single(category => category.UpstreamCategoryId == "30");

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category20.Id, sourceId, ContentType.Vod, false, category10.DedicatedOutputCategoryId, "Movies"));
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category30.Id, sourceId, ContentType.Vod, false, category10.DedicatedOutputCategoryId, "Movies"));
        }

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

        Assert.Single(result);
        Assert.Equal("Movies", result[0].CategoryName);
        Assert.Equal(["10", "30"], result[0].IncludedUpstreamCategoryIds.OrderBy(value => value).ToList());
        Assert.Single(mappings);
        Assert.Equal(["10", "30"], mappings[0].IncludedUpstreamCategoryIds.OrderBy(value => value).ToList());
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

    private static async Task<int> GetSourceIdAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
    }
}
