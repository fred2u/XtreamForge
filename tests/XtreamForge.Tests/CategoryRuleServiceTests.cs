using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Data;
using XtreamForge.Categories;
using XtreamForge.Source;

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

        var mappingService = serviceProvider.GetRequiredService<CategoryService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider, "example.com");

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

        var mappingService = serviceProvider.GetRequiredService<CategoryService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider, "example.com");

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

        var mappingService = serviceProvider.GetRequiredService<CategoryService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider, "example.com");

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

        var mappingService = serviceProvider.GetRequiredService<CategoryService>();
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

        var mappingService = serviceProvider.GetRequiredService<CategoryService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ruleService.DeleteRuleAsync(new CategoryRuleDeleteCommand(99999, sourceId, ContentType.Vod, true)));

        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReorderRulesAsync_PersistsNormalizedSequenceAndChangesFirstMatchOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<CategoryService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "SPORT", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));

        var originalRules = await GetRulesAsync(serviceProvider);
        Assert.Equal(CategoryInclusionDecision.Include, (await ruleService.PreviewAsync(sourceId, ContentType.Vod, "|FR| SPORT"))!.Evaluation.Decision);

        await ruleService.ReorderRulesAsync(new CategoryRuleOrderCommand(sourceId, ContentType.Vod, originalRules.OrderByDescending(rule => rule.Sequence).Select(rule => rule.Id).ToArray()));

        var reorderedRules = await GetRulesAsync(serviceProvider);
        Assert.Equal([10, 20], reorderedRules.Select(rule => rule.Sequence).ToArray());
        Assert.Equal(CategoryRuleAction.Exclude, reorderedRules[0].Action);
        Assert.Equal(CategoryInclusionDecision.Exclude, (await ruleService.PreviewAsync(sourceId, ContentType.Vod, "|FR| SPORT"))!.Evaluation.Decision);
    }

    [Fact]
    public async Task UpdateRuleAsync_ReEnablingRule_RestoresItsPersistedPositionAndEvaluation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<CategoryService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, false));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "SPORT", false, true));

        var disabledFirstRule = (await GetRulesAsync(serviceProvider)).Single(rule => rule.Sequence == 10);
        Assert.Equal(CategoryInclusionDecision.Include, (await ruleService.PreviewAsync(sourceId, ContentType.Vod, "|FR| SPORT"))!.Evaluation.Decision);

        await ruleService.UpdateRuleAsync(new CategoryRuleEditorCommand(
            disabledFirstRule.Id,
            sourceId,
            ContentType.Vod,
            disabledFirstRule.Action,
            disabledFirstRule.Operator,
            disabledFirstRule.Pattern,
            disabledFirstRule.CaseSensitive,
            true));

        var reEnabledRules = await GetRulesAsync(serviceProvider);
        Assert.Equal([10, 20], reEnabledRules.Select(rule => rule.Sequence).ToArray());
        Assert.True(reEnabledRules.Single(rule => rule.Id == disabledFirstRule.Id).IsEnabled);
        Assert.Equal(CategoryInclusionDecision.Exclude, (await ruleService.PreviewAsync(sourceId, ContentType.Vod, "|FR| SPORT"))!.Evaluation.Decision);
        Assert.Equal(disabledFirstRule.Id, (await ruleService.PreviewAsync(sourceId, ContentType.Vod, "|FR| SPORT"))!.Evaluation.MatchedRuleId);
    }

    [Fact]
    public async Task ReorderRulesAsync_RejectsRulesFromDifferentScopes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<CategoryService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await mappingService.RewriteCategoriesAsync(new XtreamSourceDescriptor("https", "example.com", 443), ContentType.Vod, [new DiscoveredCategory("42", "SPORT")]);
        await mappingService.RewriteCategoriesAsync(new XtreamSourceDescriptor("https", "other.example", 443), ContentType.Vod, [new DiscoveredCategory("50", "NEWS")]);

        var sourceId = await GetSourceIdAsync(serviceProvider, "example.com");
        var otherSourceId = await GetSourceIdAsync(serviceProvider, "other.example");

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "SPORT", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "MOVIES", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Series, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, otherSourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "NEWS", false, true));

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var targetRuleIds = await dbContext.CategoryRules
            .Where(rule => rule.XtreamSourceId == sourceId && rule.ContentType == ContentType.Vod)
            .OrderBy(rule => rule.Sequence)
            .Select(rule => rule.Id)
            .ToListAsync();
        var wrongSourceRuleId = await dbContext.CategoryRules.Where(rule => rule.XtreamSourceId == otherSourceId && rule.ContentType == ContentType.Vod).Select(rule => rule.Id).SingleAsync();
        var wrongTypeRuleId = await dbContext.CategoryRules.Where(rule => rule.XtreamSourceId == sourceId && rule.ContentType == ContentType.Series).Select(rule => rule.Id).SingleAsync();

        var wrongSourceException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ruleService.ReorderRulesAsync(new CategoryRuleOrderCommand(sourceId, ContentType.Vod, [targetRuleIds[0], wrongSourceRuleId])));
        Assert.Contains("selected source or content type", wrongSourceException.Message, StringComparison.OrdinalIgnoreCase);

        var wrongTypeException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ruleService.ReorderRulesAsync(new CategoryRuleOrderCommand(sourceId, ContentType.Vod, [targetRuleIds[0], wrongTypeRuleId])));
        Assert.Contains("selected source or content type", wrongTypeException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReorderRulesAsync_RejectsDuplicateAndIncompleteOrderPayloads()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var mappingService = serviceProvider.GetRequiredService<CategoryService>();
        var ruleService = serviceProvider.GetRequiredService<CategoryRuleService>();
        await SeedVodCategoriesAsync(mappingService);
        var sourceId = await GetSourceIdAsync(serviceProvider);

        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true));
        await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));

        var ruleIds = (await GetRulesAsync(serviceProvider)).Select(rule => rule.Id).ToArray();

        var duplicateException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ruleService.ReorderRulesAsync(new CategoryRuleOrderCommand(sourceId, ContentType.Vod, [ruleIds[0], ruleIds[0]])));
        Assert.Contains("unique", duplicateException.Message, StringComparison.OrdinalIgnoreCase);

        var incompleteException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ruleService.ReorderRulesAsync(new CategoryRuleOrderCommand(sourceId, ContentType.Vod, [ruleIds[0]])));
        Assert.Contains("every category rule", incompleteException.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider CreateServiceProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CategoryRuleEvaluator());
        services.AddDbContextFactory<XtreamForgeDbContext>(options => options.UseSqlite(connection));
        services.AddScoped(static provider => provider.GetRequiredService<IDbContextFactory<XtreamForgeDbContext>>().CreateDbContext());
        services.AddSingleton<SourceService>();
        services.AddScoped<CategoryService>();
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

    private static async Task SeedVodCategoriesAsync(CategoryService mappingService)
    {
        await mappingService.RewriteCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [new DiscoveredCategory("42", "|FR| SPORT")]);
    }

    private static async Task<int> GetSourceIdAsync(ServiceProvider serviceProvider, string? host = null)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var query = dbContext.XtreamSources.AsQueryable();
        if (!string.IsNullOrWhiteSpace(host))
        {
            query = query.Where(source => source.Host == host);
        }

        return await query.Select(source => source.Id).SingleAsync();
    }

    private static async Task<CategoryRule> GetRuleAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.CategoryRules.AsNoTracking().SingleAsync();
    }

    private static async Task<List<CategoryRule>> GetRulesAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        return await dbContext.CategoryRules.AsNoTracking().OrderBy(rule => rule.Sequence).ToListAsync();
    }
}
