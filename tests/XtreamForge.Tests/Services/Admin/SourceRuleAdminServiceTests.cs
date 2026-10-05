using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Rules;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Admin;

public sealed class CategoryRuleAdminServiceTests : SourceRuleAdminServiceTests<CategoryRule>;

public sealed class ItemRuleAdminServiceTests : SourceRuleAdminServiceTests<ItemRule>;

/// <summary>Tests of <see cref="SourceRuleAdminService{TRule}"/>, run for the category rules and for the item rules.</summary>
public abstract class SourceRuleAdminServiceTests<TRule> : IAsyncDisposable where TRule : class, ISourceRule, new()
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly SourceRuleAdminService<TRule> _service;

    protected SourceRuleAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _service = new SourceRuleAdminService<TRule>(_dbContext);
    }

    // ─── GetBySourceAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetBySourceAsync_ReturnsOnlyRulesMatchingSourceAndContentType()
    {
        var source1 = await AddSourceAsync("s1.example.com");
        var source2 = await AddSourceAsync("s2.example.com");
        await AddRuleAsync(source1.Id, ContentType.Vod, 1, "a");
        await AddRuleAsync(source1.Id, ContentType.Vod, 2, "b");
        await AddRuleAsync(source1.Id, ContentType.Series, 1, "c");
        await AddRuleAsync(source2.Id, ContentType.Vod, 1, "d");

        var result = await _service.GetBySourceAsync(source1.Id, ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], result.Select(r => r.Pattern).ToList());
    }

    [Fact]
    public async Task GetBySourceAsync_ReturnsRulesOrderedBySequence()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 30, "c");
        await AddRuleAsync(source.Id, ContentType.Vod, 10, "a");
        await AddRuleAsync(source.Id, ContentType.Vod, 20, "b");

        var result = await _service.GetBySourceAsync(source.Id, ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Equal([10, 20, 30], result.Select(r => r.Sequence).ToList());
    }

    // ─── CreateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WhenSourceNotFound_ReturnsNullAndNoConflict()
    {
        var (rule, sequenceConflict) = await _service.CreateAsync(999, ContentType.Vod, Values(1, "x"), TestContext.Current.CancellationToken);

        Assert.Null(rule);
        Assert.False(sequenceConflict);
    }

    [Fact]
    public async Task CreateAsync_WithUniqueSequence_PersistsRule()
    {
        var source = await AddSourceAsync();

        var (rule, sequenceConflict) = await _service.CreateAsync(
            source.Id,
            ContentType.Series,
            new RuleValues(3, RuleAction.Exclude, RuleOperator.StartsWith, "[XXX]", CaseSensitive: true, IsEnabled: false),
            TestContext.Current.CancellationToken);

        Assert.False(sequenceConflict);
        Assert.NotNull(rule);

        var persisted = await _dbContext.Set<TRule>().AsNoTracking().SingleAsync(r => r.Id == rule.Id, TestContext.Current.CancellationToken);
        Assert.Equal(source.Id, persisted.XtreamSourceId);
        Assert.Equal(ContentType.Series, persisted.ContentType);
        Assert.Equal(3, persisted.Sequence);
        Assert.Equal(RuleAction.Exclude, persisted.Action);
        Assert.Equal(RuleOperator.StartsWith, persisted.Operator);
        Assert.Equal("[XXX]", persisted.Pattern);
        Assert.True(persisted.CaseSensitive);
        Assert.False(persisted.IsEnabled);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateSequenceForSameSourceAndContentType_ReturnsSequenceConflict()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 1, "existing");

        var (rule, sequenceConflict) = await _service.CreateAsync(source.Id, ContentType.Vod, Values(1, "dup"), TestContext.Current.CancellationToken);

        Assert.True(sequenceConflict);
        Assert.Null(rule);
        Assert.Equal(1, await _dbContext.Set<TRule>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_SameSequenceOnDifferentContentType_Succeeds()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 1, "existing");

        var (rule, sequenceConflict) = await _service.CreateAsync(source.Id, ContentType.Series, Values(1, "other"), TestContext.Current.CancellationToken);

        Assert.False(sequenceConflict);
        Assert.NotNull(rule);
    }

    [Fact]
    public async Task CreateAsync_SameSequenceOnDifferentSource_Succeeds()
    {
        var source1 = await AddSourceAsync("s1.example.com");
        var source2 = await AddSourceAsync("s2.example.com");
        await AddRuleAsync(source1.Id, ContentType.Vod, 1, "existing");

        var (rule, sequenceConflict) = await _service.CreateAsync(source2.Id, ContentType.Vod, Values(1, "other"), TestContext.Current.CancellationToken);

        Assert.False(sequenceConflict);
        Assert.NotNull(rule);
    }

    // ─── UpdateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WhenNotFound_ReturnsNotFound()
    {
        var (found, sequenceConflict) = await _service.UpdateAsync(999, Values(1, "x"), TestContext.Current.CancellationToken);

        Assert.False(found);
        Assert.False(sequenceConflict);
    }

    [Fact]
    public async Task UpdateAsync_WithUniqueSequence_UpdatesAllFields()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "old");

        var (found, sequenceConflict) = await _service.UpdateAsync(
            rule.Id,
            new RuleValues(5, RuleAction.Exclude, RuleOperator.StartsWith, "new", CaseSensitive: true, IsEnabled: false),
            TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.False(sequenceConflict);

        var updated = await _dbContext.Set<TRule>().AsNoTracking().SingleAsync(r => r.Id == rule.Id, TestContext.Current.CancellationToken);
        Assert.Equal(5, updated.Sequence);
        Assert.Equal(RuleAction.Exclude, updated.Action);
        Assert.Equal(RuleOperator.StartsWith, updated.Operator);
        Assert.Equal("new", updated.Pattern);
        Assert.True(updated.CaseSensitive);
        Assert.False(updated.IsEnabled);
        Assert.Equal(ContentType.Vod, updated.ContentType);
    }

    [Fact]
    public async Task UpdateAsync_WithDuplicateSequence_ReturnsSequenceConflictAndKeepsRule()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");

        var (found, sequenceConflict) = await _service.UpdateAsync(rule.Id, Values(1, "changed"), TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.True(sequenceConflict);

        var unchanged = await _dbContext.Set<TRule>().AsNoTracking().SingleAsync(r => r.Id == rule.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, unchanged.Sequence);
        Assert.Equal("b", unchanged.Pattern);
    }

    [Fact]
    public async Task UpdateAsync_WithOwnCurrentSequence_Succeeds()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "x");

        var (found, sequenceConflict) = await _service.UpdateAsync(rule.Id, Values(1, "x"), TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.False(sequenceConflict);
    }

    // ─── DeleteAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenExists_ReturnsTrueAndRemovesFromDatabase()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "x");

        var deleted = await _service.DeleteAsync(rule.Id, TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.False(await _dbContext.Set<TRule>().AnyAsync(r => r.Id == rule.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_WhenNotFound_ReturnsFalse()
    {
        var deleted = await _service.DeleteAsync(999, TestContext.Current.CancellationToken);

        Assert.False(deleted);
    }

    // ─── ReorderAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReorderAsync_WhenSourceNotFound_ReturnsSourceNotFound()
    {
        var (result, rules) = await _service.ReorderAsync(999, ContentType.Vod, [], TestContext.Current.CancellationToken);

        Assert.Equal(RuleReorderResult.SourceNotFound, result);
        Assert.Empty(rules);
    }

    [Fact]
    public async Task ReorderAsync_RenumbersTheRulesOfTheSourceAndContentTypeInTheGivenOrder()
    {
        var source = await AddSourceAsync();
        var first = await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var second = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");
        var series = await AddRuleAsync(source.Id, ContentType.Series, 1, "s");

        var (result, rules) = await _service.ReorderAsync(source.Id, ContentType.Vod, [second.Id, first.Id], TestContext.Current.CancellationToken);

        Assert.Equal(RuleReorderResult.Reordered, result);
        Assert.Equal([(second.Id, RuleSequences.Step), (first.Id, 2 * RuleSequences.Step)], rules.Select(rule => (rule.Id, rule.Sequence)));
        Assert.Equal(1, (await _dbContext.Set<TRule>().AsNoTracking().SingleAsync(r => r.Id == series.Id, TestContext.Current.CancellationToken)).Sequence);
    }

    private static RuleValues Values(int sequence, string pattern) =>
        new(sequence, RuleAction.Include, RuleOperator.Contains, pattern, CaseSensitive: false, IsEnabled: true);

    private async Task<XtreamSource> AddSourceAsync(string host = "s.example.com")
    {
        var source = new XtreamSource { Protocol = "http", Host = host, Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return source;
    }

    private async Task<TRule> AddRuleAsync(int sourceId, ContentType contentType, int sequence, string pattern)
    {
        var rule = new TRule
        {
            XtreamSourceId = sourceId,
            ContentType = contentType,
            Sequence = sequence,
            Action = RuleAction.Include,
            Operator = RuleOperator.Contains,
            Pattern = pattern,
            IsEnabled = true
        };
        _dbContext.Set<TRule>().Add(rule);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return rule;
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
