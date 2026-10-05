using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Endpoints.Admin.Rules;
using XtreamForge.ApiService.Endpoints.Admin.Rules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Rules;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public sealed class CategoryRuleEndpointsTests() : SourceRuleEndpointsTests<CategoryRule>("category-rules");

public sealed class ItemRuleEndpointsTests() : SourceRuleEndpointsTests<ItemRule>("item-rules");

/// <summary>Tests of <see cref="SourceRuleEndpoints"/>, run for the category rules and for the item rules.</summary>
public abstract class SourceRuleEndpointsTests<TRule> : IAsyncDisposable where TRule : class, ISourceRule, new()
{
    private readonly string _segment;
    private readonly XtreamForgeDbContext _dbContext;
    private readonly SourceRuleAdminService<TRule> _ruleAdminService;

    protected SourceRuleEndpointsTests(string segment)
    {
        _segment = segment;
        _dbContext = SqliteDbContextFactory.Create();
        _ruleAdminService = new SourceRuleAdminService<TRule>(_dbContext);
    }

    // ─── GET /api/admin/sources/{sourceId}/{segment} ────────────────────────

    [Fact]
    public async Task GetAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var result = await SourceRuleEndpoints.GetAsync(999, ContentType.Vod, _ruleAdminService, new SourceAdminService(_dbContext), TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GetAsync_WhenSourceExists_ReturnsOkWithRulesOfContentType()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "vod");
        await AddRuleAsync(source.Id, ContentType.Series, 1, "series");

        var result = await SourceRuleEndpoints.GetAsync(source.Id, ContentType.Vod, _ruleAdminService, new SourceAdminService(_dbContext), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<AdminRuleDto>>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal(
            new AdminRuleDto(rule.Id, source.Id, ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "vod", false, true, null),
            Assert.Single(ok.Value));
    }

    // ─── POST /api/admin/sources/{sourceId}/{segment} ───────────────────────

    [Fact]
    public async Task PostAsync_WhenValid_ReturnsCreatedWithLocationAndDto()
    {
        var source = await AddSourceAsync();
        var request = new AdminRuleRequest(ContentType.Series, 3, RuleAction.Exclude, RuleOperator.StartsWith, "Kids", true, false);

        var result = await PostAsync(source.Id, request);

        var created = Assert.IsType<Created<AdminRuleDto>>(result);
        Assert.NotNull(created.Value);
        Assert.Equal($"/api/admin/sources/{source.Id}/{_segment}/{created.Value.Id}", created.Location);
        Assert.Equal(
            new AdminRuleDto(created.Value.Id, source.Id, ContentType.Series, 3, RuleAction.Exclude, RuleOperator.StartsWith, "Kids", true, false, null),
            created.Value);
    }

    [Fact]
    public async Task PostAsync_IgnoresTheField()
    {
        var source = await AddSourceAsync();
        var request = Request(ContentType.Vod, 1, "x") with { Field = (TmdbRuleField)99 };

        var result = await PostAsync(source.Id, request);

        Assert.Null(Assert.IsType<Created<AdminRuleDto>>(result).Value?.Field);
    }

    [Fact]
    public async Task PostAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var result = await PostAsync(999, Request(ContentType.Vod, 1, "x"));

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PostAsync_WhenSequenceAlreadyUsed_ReturnsConflict()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 1, "existing");

        var result = await PostAsync(source.Id, Request(ContentType.Vod, 1, "x"));

        Assert.IsType<Conflict>(result);
    }

    [Theory]
    [InlineData(ContentType.Undefined, RuleAction.Include, RuleOperator.Contains, "x", nameof(AdminRuleRequest.ContentType))]
    [InlineData((ContentType)99, RuleAction.Include, RuleOperator.Contains, "x", nameof(AdminRuleRequest.ContentType))]
    [InlineData(ContentType.Vod, (RuleAction)99, RuleOperator.Contains, "x", nameof(AdminRuleRequest.Action))]
    [InlineData(ContentType.Vod, RuleAction.Include, (RuleOperator)99, "x", nameof(AdminRuleRequest.Operator))]
    [InlineData(ContentType.Vod, RuleAction.Include, RuleOperator.Contains, null, nameof(AdminRuleRequest.Pattern))]
    [InlineData(ContentType.Vod, RuleAction.Include, RuleOperator.Contains, " ", nameof(AdminRuleRequest.Pattern))]
    public async Task PostAndPutAsync_WhenInvalid_ReturnValidationProblemAndChangeNothing(
        ContentType contentType,
        RuleAction action,
        RuleOperator @operator,
        string? pattern,
        string invalidProperty)
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "existing");
        var request = new AdminRuleRequest(contentType, 2, action, @operator, pattern, false, true);

        var postResult = await PostAsync(source.Id, request);
        var putResult = await SourceRuleEndpoints.PutAsync(rule.Id, request, _ruleAdminService, TestContext.Current.CancellationToken);

        Assert.Contains(invalidProperty, Assert.IsType<ValidationProblem>(postResult).ProblemDetails.Errors.Keys);
        Assert.Contains(invalidProperty, Assert.IsType<ValidationProblem>(putResult).ProblemDetails.Errors.Keys);
        var stored = Assert.Single(await _dbContext.Set<TRule>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal((rule.Id, 1, "existing"), (stored.Id, stored.Sequence, stored.Pattern));
    }

    [Fact]
    public async Task PostAsync_WhenPatternIsTooLong_ReturnsValidationProblem()
    {
        var source = await AddSourceAsync();

        var result = await PostAsync(source.Id, Request(ContentType.Vod, 1, new string('x', AdminRuleRequest.PatternMaxLength + 1)));

        Assert.Contains(nameof(AdminRuleRequest.Pattern), Assert.IsType<ValidationProblem>(result).ProblemDetails.Errors.Keys);
    }

    // ─── PUT /api/admin/{segment}/{id} ──────────────────────────────────────

    [Fact]
    public async Task PutAsync_WhenValid_ReturnsNoContent()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "old");

        var result = await SourceRuleEndpoints.PutAsync(rule.Id, Request(ContentType.Vod, 2, "new"), _ruleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task PutAsync_WhenRuleDoesNotExist_ReturnsNotFound()
    {
        var result = await SourceRuleEndpoints.PutAsync(999, Request(ContentType.Vod, 1, "x"), _ruleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PutAsync_WhenSequenceAlreadyUsed_ReturnsConflict()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");

        var result = await SourceRuleEndpoints.PutAsync(rule.Id, Request(ContentType.Vod, 1, "b"), _ruleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<Conflict>(result);
    }

    // ─── DELETE /api/admin/{segment}/{id} ───────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenRuleExists_ReturnsNoContent()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "x");

        var result = await SourceRuleEndpoints.DeleteAsync(rule.Id, _ruleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenRuleDoesNotExist_ReturnsNotFound()
    {
        var result = await SourceRuleEndpoints.DeleteAsync(999, _ruleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    // ─── PUT /api/admin/sources/{sourceId}/{segment}/order ──────────────────

    [Fact]
    public async Task PutOrderAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var result = await PutOrderAsync(999, new AdminRuleOrderRequest(ContentType.Vod, []));

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PutOrderAsync_WhenContentTypeIsUndefined_ReturnsValidationProblem()
    {
        var source = await AddSourceAsync();

        var result = await PutOrderAsync(source.Id, new AdminRuleOrderRequest(ContentType.Undefined, []));

        Assert.IsType<ValidationProblem>(result);
    }

    [Fact]
    public async Task PutOrderAsync_WhenOrderIsComplete_AssignsSequencesInOrderAndReturnsRules()
    {
        var source = await AddSourceAsync();
        var first = await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var second = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");
        var third = await AddRuleAsync(source.Id, ContentType.Vod, 3, "c");

        var result = await PutOrderAsync(source.Id, new AdminRuleOrderRequest(ContentType.Vod, [third.Id, first.Id, second.Id]));

        var ok = Assert.IsType<Ok<IEnumerable<AdminRuleDto>>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal([(third.Id, 10), (first.Id, 20), (second.Id, 30)], ok.Value.Select(r => (r.Id, r.Sequence)).ToList());
        Assert.Equal([(third.Id, 10), (first.Id, 20), (second.Id, 30)], await GetStoredOrderAsync(source.Id, ContentType.Vod));
    }

    [Fact]
    public async Task PutOrderAsync_WhenSwappingRulesWithFinalSequences_DoesNotConflict()
    {
        var source = await AddSourceAsync();
        var first = await AddRuleAsync(source.Id, ContentType.Vod, 10, "a");
        var second = await AddRuleAsync(source.Id, ContentType.Vod, 20, "b");

        var result = await PutOrderAsync(source.Id, new AdminRuleOrderRequest(ContentType.Vod, [second.Id, first.Id]));

        Assert.IsType<Ok<IEnumerable<AdminRuleDto>>>(result);
        Assert.Equal([(second.Id, 10), (first.Id, 20)], await GetStoredOrderAsync(source.Id, ContentType.Vod));
    }

    [Fact]
    public async Task PutOrderAsync_DoesNotChangeRulesOfOtherContentTypeOrSource()
    {
        var source = await AddSourceAsync();
        var otherSource = await AddSourceAsync("o.example.com");
        var first = await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var second = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");
        var series = await AddRuleAsync(source.Id, ContentType.Series, 1, "s");
        var other = await AddRuleAsync(otherSource.Id, ContentType.Vod, 1, "o");

        await PutOrderAsync(source.Id, new AdminRuleOrderRequest(ContentType.Vod, [second.Id, first.Id]));

        Assert.Equal([(series.Id, 1)], await GetStoredOrderAsync(source.Id, ContentType.Series));
        Assert.Equal([(other.Id, 1)], await GetStoredOrderAsync(otherSource.Id, ContentType.Vod));
    }

    [Fact]
    public async Task PutOrderAsync_WhenRuleIsMissing_ReturnsValidationProblemAndChangesNothing()
    {
        var source = await AddSourceAsync();
        var first = await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var second = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");
        await AddRuleAsync(source.Id, ContentType.Vod, 3, "c");

        await AssertOrderRejectedAsync(source.Id, [second.Id, first.Id]);
    }

    [Fact]
    public async Task PutOrderAsync_WhenRuleIsDuplicated_ReturnsValidationProblemAndChangesNothing()
    {
        var source = await AddSourceAsync();
        var first = await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var second = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");

        await AssertOrderRejectedAsync(source.Id, [second.Id, first.Id, second.Id]);
    }

    [Fact]
    public async Task PutOrderAsync_WhenRuleBelongsToOtherContentType_ReturnsValidationProblemAndChangesNothing()
    {
        var source = await AddSourceAsync();
        var first = await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var series = await AddRuleAsync(source.Id, ContentType.Series, 2, "s");

        await AssertOrderRejectedAsync(source.Id, [series.Id, first.Id]);
    }

    [Fact]
    public async Task PutOrderAsync_WhenRuleBelongsToOtherSource_ReturnsValidationProblemAndChangesNothing()
    {
        var source = await AddSourceAsync();
        var otherSource = await AddSourceAsync("o.example.com");
        var first = await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var other = await AddRuleAsync(otherSource.Id, ContentType.Vod, 2, "o");

        await AssertOrderRejectedAsync(source.Id, [other.Id, first.Id]);
    }

    [Fact]
    public async Task PutOrderAsync_WhenRuleIdsAreMissing_ReturnsValidationProblem()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");

        var result = await PutOrderAsync(source.Id, new AdminRuleOrderRequest(ContentType.Vod, null));

        Assert.IsType<ValidationProblem>(result);
    }

    private async Task AssertOrderRejectedAsync(int sourceId, IReadOnlyList<int> ruleIds)
    {
        var before = await GetStoredOrderAsync(sourceId, ContentType.Vod);

        var result = await PutOrderAsync(sourceId, new AdminRuleOrderRequest(ContentType.Vod, ruleIds));

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Contains(nameof(AdminRuleOrderRequest.RuleIds), problem.ProblemDetails.Errors.Keys);
        Assert.Equal(before, await GetStoredOrderAsync(sourceId, ContentType.Vod));
    }

    private Task<IResult> PostAsync(int sourceId, AdminRuleRequest request)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = $"/api/admin/sources/{sourceId}/{_segment}";

        return SourceRuleEndpoints.PostAsync(sourceId, request, _ruleAdminService, httpContext.Request, TestContext.Current.CancellationToken);
    }

    private Task<IResult> PutOrderAsync(int sourceId, AdminRuleOrderRequest request) =>
        SourceRuleEndpoints.PutOrderAsync(sourceId, request, _ruleAdminService, TestContext.Current.CancellationToken);

    private static AdminRuleRequest Request(ContentType contentType, int sequence, string pattern) =>
        new(contentType, sequence, RuleAction.Include, RuleOperator.Contains, pattern, false, true);

    private async Task<List<(int Id, int Sequence)>> GetStoredOrderAsync(int sourceId, ContentType contentType)
    {
        var rules = await _dbContext.Set<TRule>()
            .AsNoTracking()
            .Where(r => r.XtreamSourceId == sourceId && r.ContentType == contentType)
            .OrderBy(r => r.Sequence)
            .ToListAsync(TestContext.Current.CancellationToken);

        return [.. rules.Select(r => (r.Id, r.Sequence))];
    }

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
