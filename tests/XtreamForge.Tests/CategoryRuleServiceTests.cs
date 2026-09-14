using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Data;
using XtreamForge.Categories;

namespace XtreamForge.Tests;

public sealed class CategoryRuleServiceTests
{
    [Theory]
    [InlineData(true, CategoryInclusionDecision.Exclude, true)]
    [InlineData(false, CategoryInclusionDecision.Include, false)]
    public async Task CreateRuleAsync_PersistsEnabledStateAcrossFreshDbContext_AndDrivesEvaluation(
        bool isEnabled,
        CategoryInclusionDecision expectedDecision,
        bool expectMatch)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(
            null,
            sourceId,
            ContentType.Vod,
            CategoryRuleAction.Exclude,
            CategoryRuleOperator.Contains,
            "SPORT",
            false,
            isEnabled));

        await using (var verifyScope = serviceProvider.CreateAsyncScope())
        {
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var persistedRule = await verifyDbContext.CategoryRules.AsNoTracking().SingleAsync();

            Assert.Equal(isEnabled, persistedRule.IsEnabled);
            Assert.Equal(CategoryRuleAction.Exclude, persistedRule.Action);
            Assert.Equal(CategoryRuleOperator.Contains, persistedRule.Operator);
            Assert.Equal("SPORT", persistedRule.Pattern);
            Assert.False(persistedRule.CaseSensitive);
        }

        var adminView = await ruleService.GetAdministrationViewAsync(sourceId, ContentType.Vod, null);
        Assert.Equal(isEnabled, Assert.Single(adminView.Rules).IsEnabled);

        var preview = await ruleService.PreviewAsync(sourceId, ContentType.Vod, "|FR| SPORT");
        Assert.NotNull(preview);
        Assert.Equal(expectedDecision, preview.Evaluation.Decision);
        Assert.Equal(expectMatch, preview.Evaluation.IsMatch);
    }

    [Fact]
    public async Task UpdateRuleAsync_ToggleEnabled_PersistsAcrossFreshDbContext_WithoutChangingOtherFields()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(
            null,
            sourceId,
            ContentType.Vod,
            CategoryRuleAction.Exclude,
            CategoryRuleOperator.Contains,
            "SPORT",
            false,
            false));

        var originalRule = await GetRuleAsync(serviceProvider);

        await ruleService.UpdateRuleAsync(new CategoryRuleEditorCommand(
            originalRule.Id,
            sourceId,
            ContentType.Vod,
            originalRule.Action,
            originalRule.Operator,
            originalRule.Pattern,
            originalRule.CaseSensitive,
            true));

        var enabledRule = await GetRuleAsync(serviceProvider);
        Assert.True(enabledRule.IsEnabled);
        Assert.Equal(originalRule.Sequence, enabledRule.Sequence);
        Assert.Equal(originalRule.Action, enabledRule.Action);
        Assert.Equal(originalRule.Operator, enabledRule.Operator);
        Assert.Equal(originalRule.Pattern, enabledRule.Pattern);
        Assert.Equal(originalRule.CaseSensitive, enabledRule.CaseSensitive);

        var enabledAdminView = await ruleService.GetAdministrationViewAsync(sourceId, ContentType.Vod, null);
        Assert.True(Assert.Single(enabledAdminView.Rules).IsEnabled);

        var enabledPreview = await ruleService.PreviewAsync(sourceId, ContentType.Vod, "|FR| SPORT");
        Assert.NotNull(enabledPreview);
        Assert.Equal(CategoryInclusionDecision.Exclude, enabledPreview.Evaluation.Decision);
        Assert.Equal(enabledRule.Id, enabledPreview.Evaluation.MatchedRuleId);

        await ruleService.UpdateRuleAsync(new CategoryRuleEditorCommand(
            enabledRule.Id,
            sourceId,
            ContentType.Vod,
            enabledRule.Action,
            enabledRule.Operator,
            enabledRule.Pattern,
            enabledRule.CaseSensitive,
            false));

        var disabledRule = await GetRuleAsync(serviceProvider);
        Assert.False(disabledRule.IsEnabled);
        Assert.Equal(originalRule.Sequence, disabledRule.Sequence);
        Assert.Equal(originalRule.Action, disabledRule.Action);
        Assert.Equal(originalRule.Operator, disabledRule.Operator);
        Assert.Equal(originalRule.Pattern, disabledRule.Pattern);
        Assert.Equal(originalRule.CaseSensitive, disabledRule.CaseSensitive);

        var disabledAdminView = await ruleService.GetAdministrationViewAsync(sourceId, ContentType.Vod, null);
        Assert.False(Assert.Single(disabledAdminView.Rules).IsEnabled);

        var disabledPreview = await ruleService.PreviewAsync(sourceId, ContentType.Vod, "|FR| SPORT");
        Assert.NotNull(disabledPreview);
        Assert.Equal(CategoryInclusionDecision.Include, disabledPreview.Evaluation.Decision);
        Assert.False(disabledPreview.Evaluation.IsMatch);
    }

    [Fact]
    public async Task UpdateRuleAsync_PersistsEditedPatternActionOperatorAndCaseSensitivity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(
            null,
            sourceId,
            ContentType.Vod,
            CategoryRuleAction.Include,
            CategoryRuleOperator.Contains,
            "DOCUMENTAIRE",
            false,
            true));

        var rule = await GetRuleAsync(serviceProvider);

        await ruleService.UpdateRuleAsync(new CategoryRuleEditorCommand(
            rule.Id,
            sourceId,
            ContentType.Vod,
            CategoryRuleAction.Exclude,
            CategoryRuleOperator.StartsWith,
            "SPORT",
            true,
            false));

        var updatedRule = await GetRuleAsync(serviceProvider);
        Assert.Equal(CategoryRuleAction.Exclude, updatedRule.Action);
        Assert.Equal(CategoryRuleOperator.StartsWith, updatedRule.Operator);
        Assert.Equal("SPORT", updatedRule.Pattern);
        Assert.True(updatedRule.CaseSensitive);
        Assert.False(updatedRule.IsEnabled);
    }

    [Fact]
    public async Task DeleteRuleAsync_RemovesPersistedRuleAndKeepsRemainingRuleOrderStable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.StartsWith, "|XXX|", false, true));

        await using (var deleteScope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = deleteScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var ruleToDelete = await dbContext.CategoryRules.SingleAsync(rule => rule.Pattern == "SPORT");
            await ruleService.DeleteRuleAsync(new CategoryRuleDeleteCommand(ruleToDelete.Id, sourceId, ContentType.Vod, true));
        }

        await using (var verifyScope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var persistedRules = await dbContext.CategoryRules.AsNoTracking().OrderBy(rule => rule.Sequence).ToListAsync();

            Assert.Equal(2, persistedRules.Count);
            Assert.DoesNotContain(persistedRules, rule => rule.Pattern == "SPORT");
            Assert.Equal([10, 20], persistedRules.Select(rule => rule.Sequence).ToArray());
        }

        var preview = await ruleService.PreviewAsync(sourceId, ContentType.Vod, "|FR| SPORT");
        Assert.NotNull(preview);
        Assert.Equal(CategoryInclusionDecision.Include, preview.Evaluation.Decision);
        Assert.False(preview.Evaluation.IsMatch);
    }

    [Fact]
    public async Task DeleteRuleAsync_WhenRuleDoesNotExist_ThrowsMeaningfulError()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ruleService.DeleteRuleAsync(new CategoryRuleDeleteCommand(99999, sourceId, ContentType.Vod, true)));

        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
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
            [new DiscoveredCategory("42", "|FR| SPORT")]);
    }

    private static async Task<int> GetSourceIdAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
    }

    private static async Task<CategoryRule> GetRuleAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.CategoryRules.AsNoTracking().SingleAsync();
    }
}
