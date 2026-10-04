using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;
using XtreamForge.ApiService.Endpoints.Admin.XtreamCategories;
using XtreamForge.ApiService.Endpoints.Admin.XtreamCategories.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class XtreamCategoryEndpointsTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly XtreamCategoryAdminService _xtreamCategoryAdminService;

    public XtreamCategoryEndpointsTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _xtreamCategoryAdminService = new XtreamCategoryAdminService(_dbContext);
    }

    // ─── GET /api/admin/sources/{sourceId}/xtream-categories ────────────────

    [Fact]
    public async Task GetAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var result = await XtreamCategoryEndpoints.GetAsync(999, ContentType.Vod, _xtreamCategoryAdminService, new SourceAdminService(_dbContext), TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GetAsync_WhenSourceExists_ReturnsOkWithCustomCategoryName()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        var category = await AddCategoryAsync(source.Id, "10", "Action", customCategory.Id);

        var result = await XtreamCategoryEndpoints.GetAsync(source.Id, ContentType.Vod, _xtreamCategoryAdminService, new SourceAdminService(_dbContext), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<AdminXtreamCategoryDto>>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal(
            [new AdminXtreamCategoryDto(category.Id, "10", "Action", ContentType.Vod, true, false, customCategory.Id, "Movies", InclusionDecision.Include, null, null)],
            ok.Value);
    }

    [Fact]
    public async Task GetAsync_WhenRuleExcludesCategory_ReturnsRuleAsExclusionReason()
    {
        var source = await AddSourceAsync();
        await AddCategoryAsync(source.Id, "10", "Kids Movies");
        var rule = await AddRuleAsync(source.Id, 3, RuleAction.Exclude, "kids");

        var dto = await GetSingleAsync(source.Id);

        Assert.Equal(InclusionDecision.Exclude, dto.Decision);
        Assert.Equal(CategoryExclusionReason.Rule, dto.ExclusionReason);
        Assert.Equal(
            new AdminCategoryRuleDto(rule.Id, source.Id, ContentType.Vod, 3, RuleAction.Exclude, RuleOperator.Contains, "kids", false, true),
            dto.DecidingRule);
    }

    [Fact]
    public async Task GetAsync_WhenEarlierRuleIncludesCategory_ReturnsIncludeRuleAsDecidingRule()
    {
        var source = await AddSourceAsync();
        await AddCategoryAsync(source.Id, "10", "Kids Movies");
        await AddRuleAsync(source.Id, 20, RuleAction.Exclude, "Kids");
        var includeRule = await AddRuleAsync(source.Id, 10, RuleAction.Include, "Movies");

        var dto = await GetSingleAsync(source.Id);

        Assert.Equal(InclusionDecision.Include, dto.Decision);
        Assert.Null(dto.ExclusionReason);
        Assert.Equal(includeRule.Id, dto.DecidingRule?.Id);
    }

    [Fact]
    public async Task GetAsync_WhenManuallyExcludedAndRuleMatches_ReturnsManualExclusionWithoutRule()
    {
        var source = await AddSourceAsync();
        await AddCategoryAsync(source.Id, "10", "Kids Movies", isExcluded: true);
        await AddRuleAsync(source.Id, 1, RuleAction.Exclude, "Kids");

        var dto = await GetSingleAsync(source.Id);

        Assert.Equal(InclusionDecision.Exclude, dto.Decision);
        Assert.Equal(CategoryExclusionReason.ManuallyExcluded, dto.ExclusionReason);
        Assert.Null(dto.DecidingRule);
    }

    [Fact]
    public async Task GetAsync_WhenDisabledByProviderAndRuleMatches_ReturnsProviderDisabledWithoutRule()
    {
        var source = await AddSourceAsync();
        await AddCategoryAsync(source.Id, "10", "Kids Movies", isEnabled: false);
        await AddRuleAsync(source.Id, 1, RuleAction.Exclude, "Kids");

        var dto = await GetSingleAsync(source.Id);

        Assert.Equal(InclusionDecision.Exclude, dto.Decision);
        Assert.Equal(CategoryExclusionReason.ProviderDisabled, dto.ExclusionReason);
        Assert.Null(dto.DecidingRule);
    }

    // ─── PATCH /api/admin/xtream-categories/{id} ────────────────────────────

    [Fact]
    public async Task PatchAsync_WhenCategoryDoesNotExist_ReturnsNotFound()
    {
        var result = await XtreamCategoryEndpoints.PatchAsync(999, new AdminXtreamCategoryPatchRequest(true, null), _xtreamCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PatchAsync_WhenCustomCategoryDoesNotExist_ReturnsValidationProblemAndSavesNothing()
    {
        var source = await AddSourceAsync();
        var category = await AddCategoryAsync(source.Id, "10", "Action");

        var result = await XtreamCategoryEndpoints.PatchAsync(category.Id, new AdminXtreamCategoryPatchRequest(true, 999), _xtreamCategoryAdminService, TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Contains(nameof(AdminXtreamCategoryPatchRequest.CustomCategoryId), problem.ProblemDetails.Errors.Keys);
        var unchanged = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);
        Assert.False(unchanged.IsExcluded);
    }

    [Fact]
    public async Task PatchAsync_WithCustomCategoryAndExclusion_ReturnsNoContentAndAppliesChanges()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        var category = await AddCategoryAsync(source.Id, "10", "Action");

        var result = await XtreamCategoryEndpoints.PatchAsync(category.Id, new AdminXtreamCategoryPatchRequest(true, customCategory.Id), _xtreamCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
        var updated = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);
        Assert.True(updated.IsExcluded);
        Assert.Equal(customCategory.Id, updated.CustomCategoryId);
    }

    [Fact]
    public async Task PatchAsync_WithUnassignCustomCategory_UnassignsCustomCategoryOnly()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        var category = await AddCategoryAsync(source.Id, "10", "Action", customCategory.Id);

        var result = await XtreamCategoryEndpoints.PatchAsync(category.Id, new AdminXtreamCategoryPatchRequest(null, null, UnassignCustomCategory: true), _xtreamCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
        var updated = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);
        Assert.Null(updated.CustomCategoryId);
        Assert.False(updated.IsExcluded);
    }

    [Fact]
    public async Task PatchAsync_WithOnlyIsExcluded_KeepsCustomCategory()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        var category = await AddCategoryAsync(source.Id, "10", "Action", customCategory.Id);

        var result = await XtreamCategoryEndpoints.PatchAsync(category.Id, new AdminXtreamCategoryPatchRequest(true, null), _xtreamCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
        var updated = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);
        Assert.Equal(customCategory.Id, updated.CustomCategoryId);
        Assert.True(updated.IsExcluded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PatchAsync_WithNonPositiveCustomCategoryId_ReturnsValidationProblemAndKeepsMapping(int customCategoryId)
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        var category = await AddCategoryAsync(source.Id, "10", "Action", customCategory.Id);

        var result = await XtreamCategoryEndpoints.PatchAsync(category.Id, new AdminXtreamCategoryPatchRequest(null, customCategoryId), _xtreamCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<ValidationProblem>(result);
        var unchanged = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);
        Assert.Equal(customCategory.Id, unchanged.CustomCategoryId);
    }

    [Fact]
    public async Task PatchAsync_WithAssignAndUnassign_ReturnsValidationProblem()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        var category = await AddCategoryAsync(source.Id, "10", "Action");

        var result = await XtreamCategoryEndpoints.PatchAsync(category.Id, new AdminXtreamCategoryPatchRequest(null, customCategory.Id, UnassignCustomCategory: true), _xtreamCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<ValidationProblem>(result);
        var unchanged = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);
        Assert.Null(unchanged.CustomCategoryId);
    }

    private async Task<AdminXtreamCategoryDto> GetSingleAsync(int sourceId)
    {
        var result = await XtreamCategoryEndpoints.GetAsync(sourceId, ContentType.Vod, _xtreamCategoryAdminService, new SourceAdminService(_dbContext), TestContext.Current.CancellationToken);
        var ok = Assert.IsType<Ok<IEnumerable<AdminXtreamCategoryDto>>>(result);
        Assert.NotNull(ok.Value);
        return Assert.Single(ok.Value);
    }

    private async Task<XtreamSource> AddSourceAsync()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync();
        return source;
    }

    private async Task<CustomCategory> AddCustomCategoryAsync(string name)
    {
        var customCategory = new CustomCategory { Name = name, ContentType = ContentType.Vod };
        _dbContext.CustomCategories.Add(customCategory);
        await _dbContext.SaveChangesAsync();
        return customCategory;
    }

    private async Task<XtreamCategory> AddCategoryAsync(int sourceId, string xtreamId, string name, int? customCategoryId = null, bool isExcluded = false, bool isEnabled = true)
    {
        var category = new XtreamCategory
        {
            XtreamSourceId = sourceId,
            XtreamId = xtreamId,
            Name = name,
            ContentType = ContentType.Vod,
            CustomCategoryId = customCategoryId,
            IsExcluded = isExcluded,
            IsEnabled = isEnabled
        };
        _dbContext.XtreamCategories.Add(category);
        await _dbContext.SaveChangesAsync();
        return category;
    }

    private async Task<CategoryRule> AddRuleAsync(int sourceId, int sequence, RuleAction action, string pattern)
    {
        var rule = new CategoryRule
        {
            XtreamSourceId = sourceId,
            ContentType = ContentType.Vod,
            Sequence = sequence,
            Action = action,
            Operator = RuleOperator.Contains,
            Pattern = pattern
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
