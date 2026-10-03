using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Endpoints.Admin.ItemRules;
using XtreamForge.ApiService.Endpoints.Admin.ItemRules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class ItemRuleEndpointsTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly ItemRuleAdminService _itemRuleAdminService;

    public ItemRuleEndpointsTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _itemRuleAdminService = new ItemRuleAdminService(_dbContext);
    }

    // ─── GET /api/admin/sources/{sourceId}/item-rules ───────────────────────

    [Fact]
    public async Task GetAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var endpoint = new ItemRulesGetEndpoint(_itemRuleAdminService, new SourceAdminService(_dbContext));

        var result = await endpoint.GetAsync(999, ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GetAsync_WhenSourceExists_ReturnsOkWithRulesOfContentType()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "vod");
        await AddRuleAsync(source.Id, ContentType.Series, 1, "series");
        var endpoint = new ItemRulesGetEndpoint(_itemRuleAdminService, new SourceAdminService(_dbContext));

        var result = await endpoint.GetAsync(source.Id, ContentType.Vod, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<AdminItemRuleDto>>>(result);
        Assert.NotNull(ok.Value);
        var dto = Assert.Single(ok.Value);
        Assert.Equal(
            new AdminItemRuleDto(rule.Id, source.Id, ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "vod", false, true),
            dto);
    }

    // ─── POST /api/admin/sources/{sourceId}/item-rules ──────────────────────

    [Fact]
    public async Task PostAsync_WhenValid_ReturnsCreatedWithLocationAndDto()
    {
        var source = await AddSourceAsync();
        var endpoint = new ItemRulePostEndpoint(_itemRuleAdminService);
        var request = new AdminItemRuleRequest(ContentType.Vod, 1, RuleAction.Exclude, RuleOperator.StartsWith, "[XXX]", true, true);

        var result = await endpoint.PostAsync(source.Id, request, TestContext.Current.CancellationToken);

        var created = Assert.IsType<Created<AdminItemRuleDto>>(result);
        Assert.NotNull(created.Value);
        Assert.Equal($"/api/admin/sources/{source.Id}/item-rules/{created.Value.Id}", created.Location);
        Assert.Equal(
            new AdminItemRuleDto(created.Value.Id, source.Id, ContentType.Vod, 1, RuleAction.Exclude, RuleOperator.StartsWith, "[XXX]", true, true),
            created.Value);
    }

    [Fact]
    public async Task PostAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var endpoint = new ItemRulePostEndpoint(_itemRuleAdminService);
        var request = new AdminItemRuleRequest(ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "x", false, true);

        var result = await endpoint.PostAsync(999, request, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PostAsync_WhenSequenceAlreadyUsed_ReturnsConflict()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 1, "existing");
        var endpoint = new ItemRulePostEndpoint(_itemRuleAdminService);
        var request = new AdminItemRuleRequest(ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "x", false, true);

        var result = await endpoint.PostAsync(source.Id, request, TestContext.Current.CancellationToken);

        Assert.IsType<Conflict>(result);
    }

    // ─── PUT /api/admin/item-rules/{id} ─────────────────────────────────────

    [Fact]
    public async Task PutAsync_WhenValid_ReturnsNoContent()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "old");
        var endpoint = new ItemRulePutEndpoint(_itemRuleAdminService);
        var request = new AdminItemRuleRequest(ContentType.Vod, 2, RuleAction.Exclude, RuleOperator.Contains, "new", false, true);

        var result = await endpoint.PutAsync(rule.Id, request, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task PutAsync_WhenRuleDoesNotExist_ReturnsNotFound()
    {
        var endpoint = new ItemRulePutEndpoint(_itemRuleAdminService);
        var request = new AdminItemRuleRequest(ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "x", false, true);

        var result = await endpoint.PutAsync(999, request, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PutAsync_WhenSequenceAlreadyUsed_ReturnsConflict()
    {
        var source = await AddSourceAsync();
        await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");
        var endpoint = new ItemRulePutEndpoint(_itemRuleAdminService);
        var request = new AdminItemRuleRequest(ContentType.Vod, 1, RuleAction.Include, RuleOperator.Contains, "b", false, true);

        var result = await endpoint.PutAsync(rule.Id, request, TestContext.Current.CancellationToken);

        Assert.IsType<Conflict>(result);
    }

    // ─── DELETE /api/admin/item-rules/{id} ──────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenRuleExists_ReturnsNoContent()
    {
        var source = await AddSourceAsync();
        var rule = await AddRuleAsync(source.Id, ContentType.Vod, 1, "x");
        var endpoint = new ItemRuleDeleteEndpoint(_itemRuleAdminService);

        var result = await endpoint.DeleteAsync(rule.Id, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenRuleDoesNotExist_ReturnsNotFound()
    {
        var endpoint = new ItemRuleDeleteEndpoint(_itemRuleAdminService);

        var result = await endpoint.DeleteAsync(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    // ─── PUT /api/admin/sources/{sourceId}/item-rules/order ─────────────────

    [Fact]
    public async Task PutOrderAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var endpoint = new ItemRulesOrderPutEndpoint(_itemRuleAdminService);

        var result = await endpoint.PutAsync(999, new AdminItemRuleOrderRequest(ContentType.Vod, []), TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PutOrderAsync_WhenContentTypeIsUndefined_ReturnsValidationProblem()
    {
        var source = await AddSourceAsync();
        var endpoint = new ItemRulesOrderPutEndpoint(_itemRuleAdminService);

        var result = await endpoint.PutAsync(source.Id, new AdminItemRuleOrderRequest(ContentType.Undefined, []), TestContext.Current.CancellationToken);

        Assert.IsType<ValidationProblem>(result);
    }

    [Fact]
    public async Task PutOrderAsync_WhenOrderIsComplete_AssignsSequencesInOrderAndReturnsRules()
    {
        var source = await AddSourceAsync();
        var first = await AddRuleAsync(source.Id, ContentType.Vod, 1, "a");
        var second = await AddRuleAsync(source.Id, ContentType.Vod, 2, "b");
        var third = await AddRuleAsync(source.Id, ContentType.Vod, 3, "c");
        var endpoint = new ItemRulesOrderPutEndpoint(_itemRuleAdminService);

        var result = await endpoint.PutAsync(source.Id, new AdminItemRuleOrderRequest(ContentType.Vod, [third.Id, first.Id, second.Id]), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<AdminItemRuleDto>>>(result);
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
        var endpoint = new ItemRulesOrderPutEndpoint(_itemRuleAdminService);

        var result = await endpoint.PutAsync(source.Id, new AdminItemRuleOrderRequest(ContentType.Vod, [second.Id, first.Id]), TestContext.Current.CancellationToken);

        Assert.IsType<Ok<IEnumerable<AdminItemRuleDto>>>(result);
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
        var endpoint = new ItemRulesOrderPutEndpoint(_itemRuleAdminService);

        await endpoint.PutAsync(source.Id, new AdminItemRuleOrderRequest(ContentType.Vod, [second.Id, first.Id]), TestContext.Current.CancellationToken);

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
        var endpoint = new ItemRulesOrderPutEndpoint(_itemRuleAdminService);

        var result = await endpoint.PutAsync(source.Id, new AdminItemRuleOrderRequest(ContentType.Vod, null), TestContext.Current.CancellationToken);

        Assert.IsType<ValidationProblem>(result);
    }

    private async Task AssertOrderRejectedAsync(int sourceId, IReadOnlyList<int> ruleIds)
    {
        var before = await GetStoredOrderAsync(sourceId, ContentType.Vod);
        var endpoint = new ItemRulesOrderPutEndpoint(_itemRuleAdminService);

        var result = await endpoint.PutAsync(sourceId, new AdminItemRuleOrderRequest(ContentType.Vod, ruleIds), TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Contains(nameof(AdminItemRuleOrderRequest.RuleIds), problem.ProblemDetails.Errors.Keys);
        Assert.Equal(before, await GetStoredOrderAsync(sourceId, ContentType.Vod));
    }

    private async Task<List<(int Id, int Sequence)>> GetStoredOrderAsync(int sourceId, ContentType contentType)
    {
        var rules = await _dbContext.ItemRules
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

    private async Task<ItemRule> AddRuleAsync(int sourceId, ContentType contentType, int sequence, string pattern)
    {
        var rule = new ItemRule
        {
            XtreamSourceId = sourceId,
            ContentType = contentType,
            Sequence = sequence,
            Action = RuleAction.Include,
            Operator = RuleOperator.Contains,
            Pattern = pattern,
            IsEnabled = true
        };
        _dbContext.ItemRules.Add(rule);
        await _dbContext.SaveChangesAsync();
        return rule;
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
