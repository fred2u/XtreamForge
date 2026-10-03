using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Admin;

public class CategoryRuleAdminServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly CategoryRuleAdminService _service;

    public CategoryRuleAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();

        _service = new CategoryRuleAdminService(_dbContext);
    }

    // ─── GetBySourceAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetBySourceAsync_ReturnsOnlyRulesMatchingSourceAndContentType()
    {
        var source1 = new XtreamSource { Protocol = "http", Host = "s1.example.com", Port = 80 };
        var source2 = new XtreamSource { Protocol = "http", Host = "s2.example.com", Port = 80 };
        _dbContext.XtreamSources.AddRange(source1, source2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.CategoryRules.AddRange(
            new CategoryRule { XtreamSourceId = source1.Id, ContentType = ContentType.Vod, Sequence = 1, Action = RuleAction.Include, Operator = RuleOperator.Contains, Pattern = "sport", IsEnabled = true },
            new CategoryRule { XtreamSourceId = source1.Id, ContentType = ContentType.Vod, Sequence = 2, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "news", IsEnabled = true },
            new CategoryRule { XtreamSourceId = source1.Id, ContentType = ContentType.Series, Sequence = 1, Action = RuleAction.Include, Operator = RuleOperator.Contains, Pattern = "series", IsEnabled = true },
            new CategoryRule { XtreamSourceId = source2.Id, ContentType = ContentType.Vod, Sequence = 1, Action = RuleAction.Include, Operator = RuleOperator.Contains, Pattern = "other", IsEnabled = true });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetBySourceAsync(source1.Id, ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(source1.Id, r.XtreamSourceId));
        Assert.All(result, r => Assert.Equal(ContentType.Vod, r.ContentType));
    }

    [Fact]
    public async Task GetBySourceAsync_ReturnsRulesOrderedBySequence()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.CategoryRules.AddRange(
            new CategoryRule { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Sequence = 30, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "c", IsEnabled = true },
            new CategoryRule { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Sequence = 10, Action = RuleAction.Include, Operator = RuleOperator.Contains, Pattern = "a", IsEnabled = true },
            new CategoryRule { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Sequence = 20, Action = RuleAction.Include, Operator = RuleOperator.Contains, Pattern = "b", IsEnabled = true });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetBySourceAsync(source.Id, ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Equal([10, 20, 30], result.Select(r => r.Sequence).ToList());
    }

    // ─── CreateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WhenSourceNotFound_ReturnsNullAndNoConflict()
    {
        var (rule, sequenceConflict) = await _service.CreateAsync(
            sourceId: 999,
            ContentType.Vod,
            sequence: 1,
            RuleAction.Include,
            RuleOperator.Contains,
            "sport",
            caseSensitive: false,
            isEnabled: true,
            TestContext.Current.CancellationToken);

        Assert.Null(rule);
        Assert.False(sequenceConflict);
    }

    [Fact]
    public async Task CreateAsync_WithUniqueSequence_ReturnsRuleAndNoConflict()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rule, sequenceConflict) = await _service.CreateAsync(
            source.Id,
            ContentType.Vod,
            sequence: 1,
            RuleAction.Include,
            RuleOperator.Contains,
            "sport",
            caseSensitive: false,
            isEnabled: true,
            TestContext.Current.CancellationToken);

        Assert.False(sequenceConflict);
        Assert.NotNull(rule);
        Assert.Equal(1, rule.Sequence);
        Assert.Equal("sport", rule.Pattern);
    }

    [Fact]
    public async Task CreateAsync_WithUniqueSequence_PersistsToDatabase()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _service.CreateAsync(source.Id, ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "sport", false, true, TestContext.Current.CancellationToken);

        Assert.True(await _dbContext.CategoryRules.AnyAsync(r => r.XtreamSourceId == source.Id && r.Sequence == 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateSequenceForSameSourceAndContentType_ReturnsSequenceConflict()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.CategoryRules.Add(new CategoryRule
        {
            XtreamSourceId = source.Id,
            ContentType = ContentType.Vod,
            Sequence = 1,
            Action = RuleAction.Include,
            Operator = RuleOperator.Contains,
            Pattern = "existing",
            IsEnabled = true
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rule, sequenceConflict) = await _service.CreateAsync(
            source.Id, ContentType.Vod, sequence: 1, RuleAction.Exclude, RuleOperator.Contains, "dup", false, true, TestContext.Current.CancellationToken);

        Assert.True(sequenceConflict);
        Assert.Null(rule);
    }

    [Fact]
    public async Task CreateAsync_SameSequenceOnDifferentContentType_Succeeds()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.CategoryRules.Add(new CategoryRule
        {
            XtreamSourceId = source.Id,
            ContentType = ContentType.Vod,
            Sequence = 1,
            Action = RuleAction.Include,
            Operator = RuleOperator.Contains,
            Pattern = "existing",
            IsEnabled = true
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rule, sequenceConflict) = await _service.CreateAsync(
            source.Id, ContentType.Series, sequence: 1, RuleAction.Include, RuleOperator.Contains, "live", false, true, TestContext.Current.CancellationToken);

        Assert.False(sequenceConflict);
        Assert.NotNull(rule);
    }

    // ─── UpdateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WhenNotFound_ReturnsNotFound()
    {
        var (found, _) = await _service.UpdateAsync(999, 1, RuleAction.Include, RuleOperator.Contains, "x", false, true, TestContext.Current.CancellationToken);

        Assert.False(found);
    }

    [Fact]
    public async Task UpdateAsync_WithUniqueSequence_UpdatesAllFields()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rule = new CategoryRule
        {
            XtreamSourceId = source.Id,
            ContentType = ContentType.Vod,
            Sequence = 1,
            Action = RuleAction.Include,
            Operator = RuleOperator.Contains,
            Pattern = "old",
            CaseSensitive = false,
            IsEnabled = true
        };
        _dbContext.CategoryRules.Add(rule);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (found, sequenceConflict) = await _service.UpdateAsync(
            rule.Id, sequence: 5, RuleAction.Exclude, RuleOperator.StartsWith, "new", caseSensitive: true, isEnabled: false, TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.False(sequenceConflict);

        var updated = await _dbContext.CategoryRules.FindAsync([rule.Id], TestContext.Current.CancellationToken);
        Assert.Equal(5, updated!.Sequence);
        Assert.Equal(RuleAction.Exclude, updated.Action);
        Assert.Equal(RuleOperator.StartsWith, updated.Operator);
        Assert.Equal("new", updated.Pattern);
        Assert.True(updated.CaseSensitive);
        Assert.False(updated.IsEnabled);
    }

    [Fact]
    public async Task UpdateAsync_WithDuplicateSequence_ReturnsSequenceConflict()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.CategoryRules.AddRange(
            new CategoryRule { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Sequence = 1, Action = RuleAction.Include, Operator = RuleOperator.Contains, Pattern = "a", IsEnabled = true },
            new CategoryRule { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Sequence = 2, Action = RuleAction.Include, Operator = RuleOperator.Contains, Pattern = "b", IsEnabled = true });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ruleToUpdate = await _dbContext.CategoryRules.SingleAsync(r => r.Sequence == 2 && r.XtreamSourceId == source.Id, TestContext.Current.CancellationToken);
        var (found, sequenceConflict) = await _service.UpdateAsync(
            ruleToUpdate.Id, sequence: 1, RuleAction.Include, RuleOperator.Contains, "b", false, true, TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.True(sequenceConflict);
    }

    [Fact]
    public async Task UpdateAsync_WithOwnCurrentSequence_Succeeds()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rule = new CategoryRule
        {
            XtreamSourceId = source.Id,
            ContentType = ContentType.Vod,
            Sequence = 1,
            Action = RuleAction.Include,
            Operator = RuleOperator.Contains,
            Pattern = "x",
            IsEnabled = true
        };
        _dbContext.CategoryRules.Add(rule);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (found, sequenceConflict) = await _service.UpdateAsync(
            rule.Id, sequence: 1, RuleAction.Exclude, RuleOperator.Contains, "x", false, true, TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.False(sequenceConflict);
    }

    // ─── DeleteAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenExists_ReturnsTrueAndRemovesFromDatabase()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rule = new CategoryRule
        {
            XtreamSourceId = source.Id,
            ContentType = ContentType.Vod,
            Sequence = 1,
            Action = RuleAction.Include,
            Operator = RuleOperator.Contains,
            Pattern = "sport",
            IsEnabled = true
        };
        _dbContext.CategoryRules.Add(rule);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var deleted = await _service.DeleteAsync(rule.Id, TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Null(await _dbContext.CategoryRules.FindAsync([rule.Id], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_WhenNotFound_ReturnsFalse()
    {
        var deleted = await _service.DeleteAsync(999, TestContext.Current.CancellationToken);

        Assert.False(deleted);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }
}
