using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Categories;
using XtreamForge.Data;
using XtreamForge.Items;
using XtreamForge.Source;

namespace XtreamForge.Tests;

public sealed class ItemRuleServiceTests
{
    [Fact]
    public async Task CreateRuleAsync_PersistsFieldAndDrivesEvaluation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<ItemRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new ItemRuleEditorCommand(
            null,
            sourceId,
            ContentType.Vod,
            ItemRuleField.Name,
            ItemRuleAction.Exclude,
            ItemRuleOperator.Contains,
            "VOST",
            false,
            true));

        await using (var verifyScope = serviceProvider.CreateAsyncScope())
        {
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var persistedRule = await verifyDbContext.ItemRules.AsNoTracking().SingleAsync();

            Assert.Equal(ItemRuleField.Name, persistedRule.Field);
            Assert.Equal(ItemRuleAction.Exclude, persistedRule.Action);
            Assert.Equal(ItemRuleOperator.Contains, persistedRule.Operator);
            Assert.Equal("VOST", persistedRule.Pattern);
        }

        var preview = await ruleService.PreviewAsync(sourceId, ContentType.Vod, "Movie VOSTFR");
        Assert.NotNull(preview);
        Assert.Equal(ItemInclusionDecision.Exclude, preview.Evaluation.Decision);
        Assert.Equal(10, preview.Evaluation.MatchedRuleSequence);
    }

    [Fact]
    public async Task ReorderAndDeleteRuleAsync_KeepsSequencesStable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<ItemRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new ItemRuleEditorCommand(null, sourceId, ContentType.Vod, ItemRuleField.Name, ItemRuleAction.Include, ItemRuleOperator.Contains, "DOCUMENT", false, true));
        await ruleService.CreateRuleAsync(new ItemRuleEditorCommand(null, sourceId, ContentType.Vod, ItemRuleField.Name, ItemRuleAction.Exclude, ItemRuleOperator.Contains, "SPORT", false, true));

        var rules = await ruleService.GetRuleDefinitionsAsync(sourceId, ContentType.Vod);
        await ruleService.ReorderRulesAsync(new ItemRuleOrderCommand(sourceId, ContentType.Vod, [rules[1].Id!.Value, rules[0].Id!.Value]));
        await ruleService.DeleteRuleAsync(new ItemRuleDeleteCommand(rules[0].Id!.Value, sourceId, ContentType.Vod, true));

        await using var verifyScope = serviceProvider.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var persistedRules = await verifyDbContext.ItemRules.AsNoTracking().OrderBy(rule => rule.Sequence).ToListAsync();

        var remainingRule = Assert.Single(persistedRules);
        Assert.Equal("SPORT", remainingRule.Pattern);
        Assert.Equal(10, remainingRule.Sequence);
    }

    private static async Task SeedVodCategoriesAsync(XtreamCategoryMappingService mappingService)
    {
        await mappingService.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("10", "Movies")
            ]);
    }

    private static async Task<int> GetSourceIdAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
    }

    private static async Task EnsureCreatedAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static ServiceProvider CreateServiceProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<XtreamForgeDbContext>(options => options.UseSqlite(connection));
        services.AddScoped(static provider => provider.GetRequiredService<IDbContextFactory<XtreamForgeDbContext>>().CreateDbContext());
        services.AddSingleton(new CategoryRuleEvaluator());
        services.AddSingleton(new ItemRuleEvaluator());
        services.AddSingleton<SourceService>();
        services.AddScoped<XtreamCategoryMappingService>();
        services.AddScoped<ItemRuleService>();
        return services.BuildServiceProvider();
    }
}
