using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Endpoints.Admin.CategoryRules;
using XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class CategoryRuleEndpointsTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly CategoryRuleAdminService _categoryRuleAdminService;

    public CategoryRuleEndpointsTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _categoryRuleAdminService = new CategoryRuleAdminService(_dbContext);
    }

    // ─── GET /api/admin/sources/{sourceId}/category-rules ───────────────────

    [Fact]
    public async Task GetAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var result = await CategoryRuleEndpoints.GetAsync(999, ContentType.Vod, _categoryRuleAdminService, new SourceAdminService(_dbContext), TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GetAsync_WhenSourceExists_ReturnsOkWithRulesOfContentType()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "vod");
        await AddRuleAsync(source.Id, ContentType.Series, 1, "series");

        var result = await CategoryRuleEndpoints.GetAsync(source.Id, ContentType.Vod, _categoryRuleAdminService, new SourceAdminService(_dbContext), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<AdminCategoryRuleDto>>>(result);
        Assert.NotNull(ok.Value);
        var dto = Assert.Single(ok.Value);
        Assert.Equal(
            new AdminCategoryRuleDto(rule.Id, source.Id, ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "vod", false, true),
            dto);
    }

    // ─── POST /api/admin/sources/{sourceId}/category-rules ──────────────────

    [Fact]
    public async Task PostAsync_WhenValid_ReturnsCreatedWithLocationAndDto()
    {
        var source = await AddSourceAsync();
        var request = new AdminCategoryRuleRequest(ContentType.Series, 3, RuleAction.Exclude, RuleOperator.StartsWith, "Kids", true, false);

        var result = await CategoryRuleEndpoints.PostAsync(source.Id, request, _categoryRuleAdminService, TestContext.Current.CancellationToken);

        var created = Assert.IsType<Created<AdminCategoryRuleDto>>(result);
        Assert.NotNull(created.Value);
        Assert.Equal($"/api/admin/sources/{source.Id}/category-rules/{created.Value.Id}", created.Location);
        Assert.Equal(
            new AdminCategoryRuleDto(created.Value.Id, source.Id, ContentType.Series, 3, RuleAction.Exclude, RuleOperator.StartsWith, "Kids", true, false),
            created.Value);
    }

    [Fact]
    public async Task PostAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var request = new AdminCategoryRuleRequest(ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "x", false, true);

        var result = await CategoryRuleEndpoints.PostAsync(999, request, _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PostAsync_WhenSequenceAlreadyUsed_ReturnsConflict()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 1, "existing");
        var request = new AdminCategoryRuleRequest(ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "x", false, true);

        var result = await CategoryRuleEndpoints.PostAsync(source.Id, request, _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<Conflict>(result);
    }

    // ─── PUT /api/admin/category-rules/{id} ─────────────────────────────────

    [Fact]
    public async Task PutAsync_WhenValid_ReturnsNoContent()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "old");
        var request = new AdminCategoryRuleRequest(ContentType.Vod, 2, RuleAction.Exclude, RuleOperator.Contains, "new", false, true);

        var result = await CategoryRuleEndpoints.PutAsync(rule.Id, request, _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task PutAsync_WhenRuleDoesNotExist_ReturnsNotFound()
    {
        var request = new AdminCategoryRuleRequest(ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "x", false, true);

        var result = await CategoryRuleEndpoints.PutAsync(999, request, _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PutAsync_WhenSequenceAlreadyUsed_ReturnsConflict()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");
        var request = new AdminCategoryRuleRequest(ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "b", false, true);

        var result = await CategoryRuleEndpoints.PutAsync(rule.Id, request, _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<Conflict>(result);
    }

    // ─── DELETE /api/admin/category-rules/{id} ──────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenRuleExists_ReturnsNoContent()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "x");

        var result = await CategoryRuleEndpoints.DeleteAsync(rule.Id, _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenRuleDoesNotExist_ReturnsNotFound()
    {
        var result = await CategoryRuleEndpoints.DeleteAsync(999, _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    // ─── PUT /api/admin/sources/{sourceId}/category-rules/order ─────────────

    [Fact]
    public async Task PutOrderAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var result = await CategoryRuleEndpoints.PutOrderAsync(999, new AdminCategoryRuleOrderRequest(ContentType.Vod, []), _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PutOrderAsync_WhenContentTypeIsUndefined_ReturnsValidationProblem()
    {
        var source = await AddSourceAsync();

        var result = await CategoryRuleEndpoints.PutOrderAsync(source.Id, new AdminCategoryRuleOrderRequest(ContentType.Undefined, []), _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<ValidationProblem>(result);
    }

    [Fact]
    public async Task PutOrderAsync_WhenOrderIsComplete_AssignsSequencesInOrderAndReturnsRules()
    {
        var source = await AddSourceAsync();
        var first = await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var second = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");
        var third = await AddRuleAsync(source.Id, ContentType.Vod, 3, "c");

        var result = await CategoryRuleEndpoints.PutOrderAsync(source.Id, new AdminCategoryRuleOrderRequest(ContentType.Vod, [third.Id, first.Id, second.Id]), _categoryRuleAdminService, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<AdminCategoryRuleDto>>>(result);
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

        var result = await CategoryRuleEndpoints.PutOrderAsync(source.Id, new AdminCategoryRuleOrderRequest(ContentType.Vod, [second.Id, first.Id]), _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<Ok<IEnumerable<AdminCategoryRuleDto>>>(result);
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

        await CategoryRuleEndpoints.PutOrderAsync(source.Id, new AdminCategoryRuleOrderRequest(ContentType.Vod, [second.Id, first.Id]), _categoryRuleAdminService, TestContext.Current.CancellationToken);

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

        var result = await CategoryRuleEndpoints.PutOrderAsync(source.Id, new AdminCategoryRuleOrderRequest(ContentType.Vod, null), _categoryRuleAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<ValidationProblem>(result);
    }

    private async Task AssertOrderRejectedAsync(int sourceId, IReadOnlyList<int> ruleIds)
    {
        var before = await GetStoredOrderAsync(sourceId, ContentType.Vod);

        var result = await CategoryRuleEndpoints.PutOrderAsync(sourceId, new AdminCategoryRuleOrderRequest(ContentType.Vod, ruleIds), _categoryRuleAdminService, TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Contains(nameof(AdminCategoryRuleOrderRequest.RuleIds), problem.ProblemDetails.Errors.Keys);
        Assert.Equal(before, await GetStoredOrderAsync(sourceId, ContentType.Vod));
    }

    private async Task<List<(int Id, int Sequence)>> GetStoredOrderAsync(int sourceId, ContentType contentType)
    {
        var rules = await _dbContext.CategoryRules
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
        await _dbContext.SaveChangesAsync();
        return source;
    }

    private async Task<CategoryRule> AddRuleAsync(int sourceId, ContentType contentType, int sequence, string pattern)
    {
        var rule = new CategoryRule
        {
            XtreamSourceId = sourceId,
            ContentType = contentType,
            Sequence = sequence,
            Action = RuleAction.Include,
            Operator = RuleOperator.Contains,
            Pattern = pattern,
            IsEnabled = true
        };
        _dbContext.CategoryRules.Add(rule);
        await _dbContext.SaveChangesAsync();
        return rule;
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
