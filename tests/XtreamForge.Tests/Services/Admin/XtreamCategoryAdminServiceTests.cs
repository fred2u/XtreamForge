using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Admin;

public class XtreamCategoryAdminServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly XtreamCategoryAdminService _service;

    public XtreamCategoryAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();

        _service = new XtreamCategoryAdminService(_dbContext);
    }

    // ─── GetBySourceAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetBySourceAsync_ReturnsOnlyCategoriesMatchingSourceAndContentType()
    {
        var source1 = new XtreamSource { Protocol = "http", Host = "s1.example.com", Port = 80 };
        var source2 = new XtreamSource { Protocol = "http", Host = "s2.example.com", Port = 80 };
        _dbContext.XtreamSources.AddRange(source1, source2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.XtreamCategories.AddRange(
            new XtreamCategory { Name = "Action", XtreamId = "1", ContentType = ContentType.Vod, XtreamSourceId = source1.Id },
            new XtreamCategory { Name = "Comedy", XtreamId = "2", ContentType = ContentType.Vod, XtreamSourceId = source1.Id },
            new XtreamCategory { Name = "News", XtreamId = "3", ContentType = ContentType.Series, XtreamSourceId = source1.Id },
            new XtreamCategory { Name = "Drama", XtreamId = "4", ContentType = ContentType.Vod, XtreamSourceId = source2.Id });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetBySourceAsync(source1.Id, ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(source1.Id, e.Category.XtreamSourceId));
        Assert.All(result, e => Assert.Equal(ContentType.Vod, e.Category.ContentType));
    }

    [Fact]
    public async Task GetBySourceAsync_ReturnsCategoriesOrderedByName()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.XtreamCategories.AddRange(
            new XtreamCategory { Name = "Zebra", XtreamId = "z", ContentType = ContentType.Vod, XtreamSourceId = source.Id },
            new XtreamCategory { Name = "Alpha", XtreamId = "a", ContentType = ContentType.Vod, XtreamSourceId = source.Id },
            new XtreamCategory { Name = "Mango", XtreamId = "m", ContentType = ContentType.Vod, XtreamSourceId = source.Id });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetBySourceAsync(source.Id, ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha", "Mango", "Zebra"], result.Select(e => e.Category.Name).ToList());
    }

    [Fact]
    public async Task GetBySourceAsync_IncludesCustomCategoryNavigation()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var custom = new CustomCategory { Name = "My Custom", ContentType = ContentType.Vod };
        _dbContext.CustomCategories.Add(custom);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.XtreamCategories.Add(new XtreamCategory
        {
            Name = "Action",
            XtreamId = "1",
            ContentType = ContentType.Vod,
            XtreamSourceId = source.Id,
            CustomCategoryId = custom.Id
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetBySourceAsync(source.Id, ContentType.Vod, TestContext.Current.CancellationToken);

        var evaluation = Assert.Single(result);
        Assert.Equal("My Custom", evaluation.Category.CustomCategory?.Name);
    }

    [Fact]
    public async Task GetBySourceAsync_EvaluatesOnlyEnabledRulesOfSameSourceAndContentType()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        var otherSource = new XtreamSource { Protocol = "http", Host = "o.example.com", Port = 80 };
        _dbContext.XtreamSources.AddRange(source, otherSource);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.XtreamCategories.Add(new XtreamCategory { Name = "Kids Movies", XtreamId = "1", ContentType = ContentType.Vod, XtreamSourceId = source.Id });
        _dbContext.CategoryRules.AddRange(
            new CategoryRule { XtreamSourceId = source.Id, ContentType = ContentType.Series, Sequence = 1, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "Kids" },
            new CategoryRule { XtreamSourceId = otherSource.Id, ContentType = ContentType.Vod, Sequence = 1, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "Kids" },
            new CategoryRule { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Sequence = 1, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "Kids", IsEnabled = false });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetBySourceAsync(source.Id, ContentType.Vod, TestContext.Current.CancellationToken);

        var evaluation = Assert.Single(result);
        Assert.Equal(InclusionDecision.Include, evaluation.Decision);
        Assert.Null(evaluation.ExclusionReason);
        Assert.Null(evaluation.DecidingRule);
    }

    [Fact]
    public async Task GetBySourceAsync_WhenRuleExcludes_ReturnsDecidingRule()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.XtreamCategories.Add(new XtreamCategory { Name = "Kids Movies", XtreamId = "1", ContentType = ContentType.Vod, XtreamSourceId = source.Id });
        var rule = new CategoryRule { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Sequence = 3, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "kids" };
        _dbContext.CategoryRules.Add(rule);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetBySourceAsync(source.Id, ContentType.Vod, TestContext.Current.CancellationToken);

        var evaluation = Assert.Single(result);
        Assert.Equal(InclusionDecision.Exclude, evaluation.Decision);
        Assert.Equal(CategoryExclusionReason.Rule, evaluation.ExclusionReason);
        Assert.Equal(rule.Id, evaluation.DecidingRule?.Id);
    }

    // ─── PatchAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PatchAsync_WhenCategoryNotFound_ReturnsCategoryNotFound()
    {
        var result = await _service.PatchAsync(999, isExcluded: true, customCategoryId: null, unassignCustomCategory: false, TestContext.Current.CancellationToken);

        Assert.Equal(XtreamCategoryPatchResult.CategoryNotFound, result);
    }

    [Fact]
    public async Task PatchAsync_WhenIsExcludedSet_UpdatesIsExcluded()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var category = new XtreamCategory { Name = "Action", XtreamId = "1", ContentType = ContentType.Vod, XtreamSourceId = source.Id };
        _dbContext.XtreamCategories.Add(category);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.PatchAsync(category.Id, isExcluded: true, customCategoryId: null, unassignCustomCategory: false, TestContext.Current.CancellationToken);

        Assert.Equal(XtreamCategoryPatchResult.Updated, result);
        var updated = await _dbContext.XtreamCategories.FindAsync([category.Id], TestContext.Current.CancellationToken);
        Assert.True(updated!.IsExcluded);
    }

    [Fact]
    public async Task PatchAsync_WhenIsExcludedNull_DoesNotChangeIsExcluded()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var category = new XtreamCategory { Name = "Action", XtreamId = "1", ContentType = ContentType.Vod, XtreamSourceId = source.Id, IsExcluded = true };
        _dbContext.XtreamCategories.Add(category);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.PatchAsync(category.Id, isExcluded: null, customCategoryId: null, unassignCustomCategory: false, TestContext.Current.CancellationToken);

        Assert.Equal(XtreamCategoryPatchResult.Updated, result);
        var updated = await _dbContext.XtreamCategories.FindAsync([category.Id], TestContext.Current.CancellationToken);
        Assert.True(updated!.IsExcluded);
    }

    [Fact]
    public async Task PatchAsync_WhenValidCustomCategoryId_AssignsCustomCategory()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var custom = new CustomCategory { Name = "My Custom", ContentType = ContentType.Vod };
        _dbContext.CustomCategories.Add(custom);
        var category = new XtreamCategory { Name = "Action", XtreamId = "1", ContentType = ContentType.Vod, XtreamSourceId = source.Id };
        _dbContext.XtreamCategories.Add(category);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.PatchAsync(category.Id, isExcluded: null, customCategoryId: custom.Id, unassignCustomCategory: false, TestContext.Current.CancellationToken);

        Assert.Equal(XtreamCategoryPatchResult.Updated, result);
        var updated = await _dbContext.XtreamCategories.FindAsync([category.Id], TestContext.Current.CancellationToken);
        Assert.Equal(custom.Id, updated!.CustomCategoryId);
    }

    [Fact]
    public async Task PatchAsync_WhenCustomCategoryIdDoesNotExist_ReturnsCustomCategoryNotFoundAndSavesNothing()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var category = new XtreamCategory { Name = "Action", XtreamId = "1", ContentType = ContentType.Vod, XtreamSourceId = source.Id };
        _dbContext.XtreamCategories.Add(category);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.PatchAsync(category.Id, isExcluded: true, customCategoryId: 999, unassignCustomCategory: false, TestContext.Current.CancellationToken);

        Assert.Equal(XtreamCategoryPatchResult.CustomCategoryNotFound, result);
        var unchanged = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);
        Assert.Null(unchanged.CustomCategoryId);
        Assert.False(unchanged.IsExcluded);
    }

    [Fact]
    public async Task PatchAsync_WhenUnassignCustomCategory_ClearsCustomCategoryId()
    {
        var source = new XtreamSource { Protocol = "http", Host = "s.example.com", Port = 80 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var custom = new CustomCategory { Name = "My Custom", ContentType = ContentType.Vod };
        _dbContext.CustomCategories.Add(custom);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var category = new XtreamCategory { Name = "Action", XtreamId = "1", ContentType = ContentType.Vod, XtreamSourceId = source.Id, CustomCategoryId = custom.Id };
        _dbContext.XtreamCategories.Add(category);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.PatchAsync(category.Id, isExcluded: null, customCategoryId: null, unassignCustomCategory: true, TestContext.Current.CancellationToken);

        Assert.Equal(XtreamCategoryPatchResult.Updated, result);
        var updated = await _dbContext.XtreamCategories.FindAsync([category.Id], TestContext.Current.CancellationToken);
        Assert.Null(updated!.CustomCategoryId);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }
}
