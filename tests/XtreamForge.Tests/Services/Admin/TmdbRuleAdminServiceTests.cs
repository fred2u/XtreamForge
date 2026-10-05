using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Admin;

public class TmdbRuleAdminServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly TmdbRuleAdminService _service;

    public TmdbRuleAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _service = new TmdbRuleAdminService(_dbContext);
    }

    [Fact]
    public async Task GetAsync_ReturnsTheRulesOfTheContentTypeBySequence()
    {
        await AddRuleAsync(ContentType.Vod, 20, "b");
        await AddRuleAsync(ContentType.Vod, 10, "a");
        await AddRuleAsync(ContentType.Series, 10, "series");

        var rules = await _service.GetAsync(ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], rules.Select(rule => rule.Pattern));
    }

    [Fact]
    public async Task CreateAsync_StoresEveryValue()
    {
        var (rule, sequenceConflict) = await _service.CreateAsync(ContentType.Series, TmdbRuleField.Genre, Values(10, "Horror"), TestContext.Current.CancellationToken);

        Assert.False(sequenceConflict);
        Assert.NotNull(rule);
        var stored = await _dbContext.TmdbRules.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            (ContentType.Series, 10, TmdbRuleField.Genre, RuleAction.Exclude, RuleOperator.Contains, "Horror", true, false),
            (stored.ContentType, stored.Sequence, stored.Field, stored.Action, stored.Operator, stored.Pattern, stored.CaseSensitive, stored.IsEnabled));
    }

    [Fact]
    public async Task CreateAsync_WhenSequenceIsUsedInTheContentType_ReturnsConflict()
    {
        await AddRuleAsync(ContentType.Vod, 10, "a");

        var (rule, sequenceConflict) = await _service.CreateAsync(ContentType.Vod, TmdbRuleField.Title, Values(10, "b"), TestContext.Current.CancellationToken);
        var (otherTypeRule, otherTypeConflict) = await _service.CreateAsync(ContentType.Series, TmdbRuleField.Title, Values(10, "c"), TestContext.Current.CancellationToken);

        Assert.Null(rule);
        Assert.True(sequenceConflict);
        Assert.NotNull(otherTypeRule);
        Assert.False(otherTypeConflict);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesTheValues()
    {
        var rule = await AddRuleAsync(ContentType.Vod, 10, "a");

        var result = await _service.UpdateAsync(rule.Id, TmdbRuleField.Genre, Values(30, "Horror"), TestContext.Current.CancellationToken);

        Assert.Equal((true, false), result);
        var stored = await _dbContext.TmdbRules.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((30, TmdbRuleField.Genre, "Horror"), (stored.Sequence, stored.Field, stored.Pattern));
    }

    [Fact]
    public async Task UpdateAsync_WhenRuleDoesNotExistOrSequenceIsUsed_ChangesNothing()
    {
        var rule = await AddRuleAsync(ContentType.Vod, 10, "a");
        await AddRuleAsync(ContentType.Vod, 20, "b");

        Assert.Equal((false, false), await _service.UpdateAsync(999, TmdbRuleField.Title, Values(30, "x"), TestContext.Current.CancellationToken));
        Assert.Equal((true, true), await _service.UpdateAsync(rule.Id, TmdbRuleField.Title, Values(20, "x"), TestContext.Current.CancellationToken));
        Assert.Equal("a", (await _dbContext.TmdbRules.AsNoTracking().SingleAsync(r => r.Id == rule.Id, TestContext.Current.CancellationToken)).Pattern);
    }

    [Fact]
    public async Task DeleteAsync_DeletesTheRule()
    {
        var rule = await AddRuleAsync(ContentType.Vod, 10, "a");

        Assert.True(await _service.DeleteAsync(rule.Id, TestContext.Current.CancellationToken));
        Assert.False(await _service.DeleteAsync(rule.Id, TestContext.Current.CancellationToken));
        Assert.False(await _dbContext.TmdbRules.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReorderAsync_RenumbersTheRulesInTheGivenOrder()
    {
        var first = await AddRuleAsync(ContentType.Vod, 10, "a");
        var second = await AddRuleAsync(ContentType.Vod, 20, "b");
        await AddRuleAsync(ContentType.Series, 10, "series");

        var (result, rules) = await _service.ReorderAsync(ContentType.Vod, [second.Id, first.Id], TestContext.Current.CancellationToken);

        Assert.Equal(RuleReorderResult.Reordered, result);
        Assert.Equal([(second.Id, 10), (first.Id, 20)], rules.Select(rule => (rule.Id, rule.Sequence)));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1, 1 })]
    public async Task ReorderAsync_WhenIdsAreNotEveryRuleExactlyOnce_ReturnsInvalidOrder(int[] ruleIds)
    {
        await AddRuleAsync(ContentType.Vod, 10, "a");
        await AddRuleAsync(ContentType.Vod, 20, "b");

        var (result, rules) = await _service.ReorderAsync(ContentType.Vod, ruleIds, TestContext.Current.CancellationToken);

        Assert.Equal(RuleReorderResult.InvalidOrder, result);
        Assert.Empty(rules);
    }

    private static RuleValues Values(int sequence, string pattern) =>
        new(sequence, RuleAction.Exclude, RuleOperator.Contains, pattern, CaseSensitive: true, IsEnabled: false);

    private async Task<TmdbRule> AddRuleAsync(ContentType contentType, int sequence, string pattern)
    {
        var rule = new TmdbRule { ContentType = contentType, Sequence = sequence, Field = TmdbRuleField.Title, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = pattern };
        _dbContext.TmdbRules.Add(rule);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return rule;
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
