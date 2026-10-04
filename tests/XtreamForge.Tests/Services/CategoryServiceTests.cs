using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Endpoints.Xtream.Dto;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services;

public class CategoryServiceTests : IAsyncDisposable
{
    private const string Protocol = "http";
    private const string Host = "provider.example.com";
    private const int Port = 8080;

    private readonly XtreamForgeDbContext _dbContext;
    private readonly CategoryService _service;

    public CategoryServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _service = new CategoryService(_dbContext);
    }

    // ─── SyncCategoriesAsync ────────────────────────────────────────────────

    [Fact]
    public async Task SyncCategoriesAsync_WhenSourceIsUnknown_CreatesSourceWithCategories()
    {
        var context = CreateContext(RequestAction.GetCategories);

        var result = await _service.SyncCategoriesAsync(
            context,
            [new XtreamCategoryDto("1", "Action"), new XtreamCategoryDto("2", "Comedy")],
            CancellationToken.None);

        var source = await _dbContext.XtreamSources
            .Include(s => s.XtreamCategories)
            .SingleAsync(s => s.Host == Host && s.Port == Port && s.Protocol == Protocol, TestContext.Current.CancellationToken);
        Assert.Equal(["1", "2"], source.XtreamCategories.Select(c => c.XtreamId).Order().ToList());
        Assert.All(source.XtreamCategories, c => Assert.Equal(ContentType.Vod, c.ContentType));

        // returned identifiers are internal identifiers, not upstream ones
        var expected = source.XtreamCategories
            .Select(c => new XtreamCategoryDto(c.Id.ToString(), c.Name))
            .OrderBy(c => c.CategoryName);
        Assert.Equal(expected, result.OrderBy(c => c.CategoryName));
    }

    [Fact]
    public async Task SyncCategoriesAsync_WhenCategoryIsNew_AddsIt()
    {
        var source = await AddSourceAsync();
        await AddCategoryAsync(source.Id, "1", "Action");

        var result = await _service.SyncCategoriesAsync(
            CreateContext(RequestAction.GetCategories),
            [new XtreamCategoryDto("1", "Action"), new XtreamCategoryDto("2", "Comedy")],
            CancellationToken.None);

        Assert.Equal(2, await _dbContext.XtreamCategories.CountAsync(c => c.XtreamSourceId == source.Id, TestContext.Current.CancellationToken));
        Assert.Equal(["Action", "Comedy"], result.Select(c => c.CategoryName).Order().ToList());
    }

    [Fact]
    public async Task SyncCategoriesAsync_WhenCategoryIsRenamed_UpdatesNameAndResetsCustomCategory()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        var category = await AddCategoryAsync(source.Id, "1", "Old name", customCategory.Id);

        var result = await _service.SyncCategoriesAsync(
            CreateContext(RequestAction.GetCategories),
            [new XtreamCategoryDto("1", "New name")],
            CancellationToken.None);

        var updated = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);
        Assert.Equal("New name", updated.Name);
        Assert.Null(updated.CustomCategoryId);
        Assert.Equal([new XtreamCategoryDto(category.Id.ToString(), "New name")], result);
    }

    [Fact]
    public async Task SyncCategoriesAsync_WhenOnlyNameCaseChanges_KeepsNameAndCustomCategory()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        var category = await AddCategoryAsync(source.Id, "1", "Action", customCategory.Id);

        await _service.SyncCategoriesAsync(
            CreateContext(RequestAction.GetCategories),
            [new XtreamCategoryDto("1", "ACTION")],
            CancellationToken.None);

        var unchanged = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Action", unchanged.Name);
        Assert.Equal(customCategory.Id, unchanged.CustomCategoryId);
    }

    [Fact]
    public async Task SyncCategoriesAsync_WhenDisabledCategoryReturnsUpstream_ReenablesItAndResetsCustomCategory()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        var category = await AddCategoryAsync(source.Id, "1", "Action", customCategory.Id);
        await _dbContext.XtreamCategories
            .Where(c => c.Id == category.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsEnabled, false), TestContext.Current.CancellationToken);

        await _service.SyncCategoriesAsync(
            CreateContext(RequestAction.GetCategories),
            [new XtreamCategoryDto("1", "Action")],
            CancellationToken.None);

        var reenabled = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);
        Assert.True(reenabled.IsEnabled);
        Assert.Null(reenabled.CustomCategoryId);
    }

    [Fact]
    public async Task SyncCategoriesAsync_WhenCategoryIsNoLongerUpstream_DisablesItAndExcludesItFromResult()
    {
        var source = await AddSourceAsync();
        var kept = await AddCategoryAsync(source.Id, "1", "Action");
        var removed = await AddCategoryAsync(source.Id, "2", "Comedy");

        var result = await _service.SyncCategoriesAsync(
            CreateContext(RequestAction.GetCategories),
            [new XtreamCategoryDto("1", "Action")],
            CancellationToken.None);

        var disabled = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == removed.Id, TestContext.Current.CancellationToken);
        Assert.False(disabled.IsEnabled);
        Assert.Equal([new XtreamCategoryDto(kept.Id.ToString(), "Action")], result);
    }

    [Fact]
    public async Task SyncCategoriesAsync_WhenTheSourceIsCreatedConcurrently_SynchronizesTheExistingSource()
    {
        // a parallel request (for example get_series_categories) creates the same source just before this one saves
        var interceptor = new BeforeFirstSaveInterceptor(async (dbContext, cancellationToken) =>
        {
            await using var concurrentDbContext = SqliteDbContextFactory.CreateOnSameDatabase(dbContext);
            concurrentDbContext.XtreamSources.Add(new XtreamSource { Protocol = Protocol, Host = Host, Port = Port });
            await concurrentDbContext.SaveChangesAsync(cancellationToken);
        });
        await using var dbContext = SqliteDbContextFactory.Create(interceptor);

        var result = await new CategoryService(dbContext).SyncCategoriesAsync(
            CreateContext(RequestAction.GetCategories),
            [new XtreamCategoryDto("1", "Action")],
            TestContext.Current.CancellationToken);

        var source = await dbContext.XtreamSources.AsNoTracking().Include(s => s.XtreamCategories).SingleAsync(TestContext.Current.CancellationToken);
        var category = Assert.Single(source.XtreamCategories);
        Assert.Equal("1", category.XtreamId);
        Assert.Equal([new XtreamCategoryDto(category.Id.ToString(), "Action")], result);
    }

    [Fact]
    public async Task SyncCategoriesAsync_WhenACategoryIsCreatedConcurrently_SynchronizesTheExistingCategory()
    {
        var interceptor = new BeforeFirstSaveInterceptor(async (dbContext, cancellationToken) =>
        {
            await using var concurrentDbContext = SqliteDbContextFactory.CreateOnSameDatabase(dbContext);
            var sourceId = await concurrentDbContext.XtreamSources.Select(s => s.Id).SingleAsync(cancellationToken);
            concurrentDbContext.XtreamCategories.Add(new XtreamCategory { XtreamSourceId = sourceId, XtreamId = "1", Name = "Action", ContentType = ContentType.Vod });
            await concurrentDbContext.SaveChangesAsync(cancellationToken);
        });
        await using var dbContext = SqliteDbContextFactory.Create(interceptor);
        await using (var seedDbContext = SqliteDbContextFactory.CreateOnSameDatabase(dbContext))
        {
            seedDbContext.XtreamSources.Add(new XtreamSource { Protocol = Protocol, Host = Host, Port = Port });
            await seedDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // the category is new when read, then created by a parallel request of the same content type before the save
        var result = await new CategoryService(dbContext).SyncCategoriesAsync(
            CreateContext(RequestAction.GetCategories),
            [new XtreamCategoryDto("1", "Action")],
            TestContext.Current.CancellationToken);

        var category = await dbContext.XtreamCategories.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal([new XtreamCategoryDto(category.Id.ToString(), "Action")], result);
    }

    [Fact]
    public async Task SyncCategoriesAsync_WhenCategoryDisappearsAndComesBack_LosesItsCustomCategory()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        var category = await AddCategoryAsync(source.Id, "1", "Action", customCategory.Id);

        await _service.SyncCategoriesAsync(CreateContext(RequestAction.GetCategories), [], TestContext.Current.CancellationToken);
        var disabled = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);

        await _service.SyncCategoriesAsync(CreateContext(RequestAction.GetCategories), [new XtreamCategoryDto("1", "Action")], TestContext.Current.CancellationToken);
        var reEnabled = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.Id == category.Id, TestContext.Current.CancellationToken);

        Assert.False(disabled.IsEnabled);
        Assert.Null(disabled.CustomCategoryId);
        Assert.True(reEnabled.IsEnabled);
        Assert.Null(reEnabled.CustomCategoryId);
    }

    [Fact]
    public async Task SyncCategoriesAsync_MatchesUpstreamIdsIgnoringCase()
    {
        var source = await AddSourceAsync();
        var category = await AddCategoryAsync(source.Id, "abc", "Action");

        await _service.SyncCategoriesAsync(
            CreateContext(RequestAction.GetCategories),
            [new XtreamCategoryDto("ABC", "Action")],
            TestContext.Current.CancellationToken);

        var stored = await _dbContext.XtreamCategories.AsNoTracking().SingleAsync(c => c.XtreamSourceId == source.Id, TestContext.Current.CancellationToken);
        Assert.Equal(category.Id, stored.Id);
        Assert.True(stored.IsEnabled);
    }

    [Fact]
    public async Task SyncCategoriesAsync_WhenCategoryHasCustomCategory_ReturnsCustomCategory()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        await AddCategoryAsync(source.Id, "1", "Action", customCategory.Id);

        var result = await _service.SyncCategoriesAsync(
            CreateContext(RequestAction.GetCategories),
            [new XtreamCategoryDto("1", "Action")],
            CancellationToken.None);

        Assert.Equal([new XtreamCategoryDto(customCategory.Id.ToString(), "Movies")], result);
    }

    [Fact]
    public async Task SyncCategoriesAsync_AppliesEnabledCategoryRulesAndExcludedFlag()
    {
        var source = await AddSourceAsync();
        await AddCategoryAsync(source.Id, "1", "Action");
        await AddCategoryAsync(source.Id, "2", "Adult", isExcluded: true);
        await AddCategoryAsync(source.Id, "3", "Sports Live");
        _dbContext.CategoryRules.Add(new CategoryRule
        {
            XtreamSourceId = source.Id,
            ContentType = ContentType.Vod,
            Sequence = 1,
            Action = RuleAction.Exclude,
            Operator = RuleOperator.StartsWith,
            Pattern = "sports",
            IsEnabled = true
        });
        await SaveAndDetachAsync();

        var result = await _service.SyncCategoriesAsync(
            CreateContext(RequestAction.GetCategories),
            [new XtreamCategoryDto("1", "Action"), new XtreamCategoryDto("2", "Adult"), new XtreamCategoryDto("3", "Sports Live")],
            CancellationToken.None);

        Assert.Equal(["Action"], result.Select(c => c.CategoryName).ToList());
    }

    // ─── GetXtreamCategoryIdMappingAsync ─────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("?category_id=all")]
    public async Task GetXtreamCategoryIdMappingAsync_WhenAllCategoriesRequested_MapsEveryVisibleCategory(string queryString)
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        await AddCategoryAsync(source.Id, "10", "Action", customCategory.Id);
        var comedy = await AddCategoryAsync(source.Id, "11", "Comedy");
        await AddCategoryAsync(source.Id, "12", "Excluded", isExcluded: true);

        var mapping = await _service.GetXtreamCategoryIdMappingAsync(CreateContext(RequestAction.GetItems, queryString), CreateSnapshot(source.Id), TestContext.Current.CancellationToken);

        Assert.Equal(2, mapping.Count);
        Assert.Equal(customCategory.Id, mapping["10"]);
        Assert.Equal(comedy.Id, mapping["11"]);
    }

    [Fact]
    public async Task GetXtreamCategoryIdMappingAsync_WhenCustomCategoryRequested_MapsItsXtreamCategories()
    {
        var source = await AddSourceAsync();
        var customCategory = await AddCustomCategoryAsync("Movies");
        await AddCategoryAsync(source.Id, "10", "Action", customCategory.Id);
        await AddCategoryAsync(source.Id, "11", "Thriller", customCategory.Id);
        await AddCategoryAsync(source.Id, "12", "Comedy");

        var mapping = await _service.GetXtreamCategoryIdMappingAsync(CreateContext(RequestAction.GetItems, $"?category_id={customCategory.Id}"), CreateSnapshot(source.Id), TestContext.Current.CancellationToken);

        Assert.Equal(["10", "11"], mapping.Keys.Order().ToList());
        Assert.All(mapping.Values, id => Assert.Equal(customCategory.Id, id));
    }

    [Fact]
    public async Task GetXtreamCategoryIdMappingAsync_AppliesTheSourceCategoryRules()
    {
        var source = await AddSourceAsync();
        await AddCategoryAsync(source.Id, "10", "Action");
        await AddCategoryAsync(source.Id, "11", "Adult");
        var excludeAdult = new CategoryRule { Sequence = 10, Action = RuleAction.Exclude, Operator = RuleOperator.StartsWith, Pattern = "Adult" };

        var mapping = await _service.GetXtreamCategoryIdMappingAsync(CreateContext(RequestAction.GetItems), CreateSnapshot(source.Id, excludeAdult), TestContext.Current.CancellationToken);

        Assert.Equal(["10"], mapping.Keys);
    }

    [Theory]
    [InlineData("?category_id=abc")]
    [InlineData("?category_id=-1")]
    [InlineData("?category_id=1.5")]
    public async Task GetXtreamCategoryIdMappingAsync_WhenCategoryIdIsNotAValidId_MapsNoCategory(string queryString)
    {
        var source = await AddSourceAsync();
        await AddCategoryAsync(source.Id, "10", "Action");

        var mapping = await _service.GetXtreamCategoryIdMappingAsync(CreateContext(RequestAction.GetItems, queryString), CreateSnapshot(source.Id), TestContext.Current.CancellationToken);

        Assert.Empty(mapping);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("?category_id=ALL", true)]
    [InlineData("?category_id=5", false)]
    public void IsGetAll_ReflectsRequestedCategory(string queryString, bool expected)
    {
        var context = CreateContext(RequestAction.GetItems, queryString);

        Assert.Equal(expected, CategoryService.IsGetAll(context));
    }

    private static XtreamContext CreateContext(RequestAction action, string queryString = "")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString(queryString);
        return new XtreamContext(Protocol, Host, Port, "player_api.php", httpContext, action, ContentType.Vod);
    }

    private static XtreamSourceSnapshot CreateSnapshot(int sourceId, params CategoryRule[] categoryRules)
        => new(sourceId, categoryRules, [], new Dictionary<string, long>(), new HashSet<string>(), []);

    private async Task<XtreamSource> AddSourceAsync()
    {
        var source = new XtreamSource { Protocol = Protocol, Host = Host, Port = Port };
        _dbContext.XtreamSources.Add(source);
        await SaveAndDetachAsync();
        return source;
    }

    private async Task<CustomCategory> AddCustomCategoryAsync(string name)
    {
        var customCategory = new CustomCategory { Name = name, ContentType = ContentType.Vod };
        _dbContext.CustomCategories.Add(customCategory);
        await SaveAndDetachAsync();
        return customCategory;
    }

    private async Task<XtreamCategory> AddCategoryAsync(
        int sourceId,
        string xtreamId,
        string name,
        int? customCategoryId = null,
        bool isExcluded = false)
    {
        var category = new XtreamCategory
        {
            XtreamSourceId = sourceId,
            XtreamId = xtreamId,
            Name = name,
            ContentType = ContentType.Vod,
            CustomCategoryId = customCategoryId,
            IsExcluded = isExcluded
        };
        _dbContext.XtreamCategories.Add(category);
        await SaveAndDetachAsync();
        return category;
    }

    // detach so the service under test loads fresh state from the database
    private async Task SaveAndDetachAsync()
    {
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
