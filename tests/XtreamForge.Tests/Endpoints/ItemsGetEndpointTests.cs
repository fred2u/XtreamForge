using System.Text.Json.Nodes;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints;

public class ItemsGetEndpointTests : IAsyncDisposable
{
    // one item per upstream category; the stub returns all of them whatever the requested category
    private const string UpstreamItems = """
        [
          { "stream_id": 1, "name": "Action movie", "category_id": "10", "tmdb_id": "101" },
          { "stream_id": 2, "name": "Comedy movie", "category_id": "11", "tmdb_id": "102" },
          { "stream_id": 3, "name": "Drama movie", "category_id": "12", "tmdb_id": "103" }
        ]
        """;

    private readonly XtreamForgeDbContext _dbContext;
    private readonly TmdbInfoQueue _tmdbInfoQueue = new();

    public ItemsGetEndpointTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Fact]
    public async Task GetAsync_WhenSourceIsUnknown_ReturnsBadRequestWithoutCallingUpstream()
    {
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK);

        var result = await CreateEndpoint(httpClientFactory).GetAsync(CreateContext(""), TestContext.Current.CancellationToken);

        Assert.IsType<BadRequest<string>>(result);
        Assert.Empty(httpClientFactory.RequestedUris);
    }

    [Theory]
    [InlineData("?category_id=abc")]
    [InlineData("?category_id=999")]
    public async Task GetAsync_WhenCategoryIdMatchesNoCategory_ReturnsBadRequestWithoutCallingUpstream(string queryString)
    {
        await SeedAsync();
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK);

        var result = await CreateEndpoint(httpClientFactory).GetAsync(CreateContext(queryString), TestContext.Current.CancellationToken);

        Assert.IsType<BadRequest<string>>(result);
        Assert.Empty(httpClientFactory.RequestedUris);
    }

    [Fact]
    public async Task GetAsync_WhenOneUpstreamCategoryIsMapped_RequestsThatCategoryOnly()
    {
        var (_, drama) = await SeedAsync();
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK);

        await CreateEndpoint(httpClientFactory).GetAsync(CreateContext($"?action=get_vod_streams&category_id={drama.Id}"), TestContext.Current.CancellationToken);

        Assert.Equal("12", GetRequestedCategoryId(Assert.Single(httpClientFactory.RequestedUris)));
    }

    [Fact]
    public async Task GetAsync_WhenSeveralUpstreamCategoriesAreMapped_RequestsAllOnceAndFiltersItems()
    {
        var (customCategory, _) = await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"), (102, "Comedy movie"), (103, "Drama movie"));
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK);
        var context = CreateContext($"?action=get_vod_streams&category_id={customCategory.Id}");

        await CreateEndpoint(httpClientFactory).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal("ALL", GetRequestedCategoryId(Assert.Single(httpClientFactory.RequestedUris)));
        var items = ReadResponseItems(context);
        Assert.Equal(["Action movie", "Comedy movie"], items.Select(item => item["name"]?.GetValue<string>()));
        Assert.All(items, item => Assert.Equal(customCategory.Id.ToString(), item["category_id"]?.GetValue<string>()));
    }

    [Fact]
    public async Task GetAsync_WhenAllCategoriesAreRequested_ReturnsEveryIncludedItem()
    {
        await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"), (102, "Comedy movie"), (103, "Drama movie"));
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK);
        var context = CreateContext("?action=get_vod_streams");

        await CreateEndpoint(httpClientFactory).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal("ALL", GetRequestedCategoryId(Assert.Single(httpClientFactory.RequestedUris)));
        Assert.Equal(3, ReadResponseItems(context).Count);
    }

    [Fact]
    public async Task GetAsync_WhenUpstreamFails_ForwardsUpstreamStatus()
    {
        await SeedAsync();
        var context = CreateContext("?action=get_vod_streams");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.Unauthorized)).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task GetAsync_ReturnsOnlyItemsEnrichedWithLoadedTmdbInfoAndEnqueuesTheOthers()
    {
        await SeedAsync();
        _dbContext.TmdbInfos.Add(new TmdbInfo
        {
            TmdbId = 101,
            ContentType = ContentType.Vod,
            Title = "Die Hard",
            ReleaseDate = new DateOnly(1988, 7, 15),
            LoadedAtUtc = DateTimeOffset.UtcNow,
            NextLoadAtUtc = DateTimeOffset.UtcNow.AddDays(60)
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var context = CreateContext("?action=get_vod_streams");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK)).GetAsync(context, TestContext.Current.CancellationToken);

        // the other items are returned by a later request, once their TMDB metadata is loaded
        Assert.Equal(["Die Hard | 1988"], ReadResponseItems(context).Select(item => item["name"]?.GetValue<string>()));
        Assert.Equal(2, _tmdbInfoQueue.Count);
    }

    [Fact]
    public async Task GetAsync_WritesEveryItemAcrossTmdbInfoBatches()
    {
        await SeedAsync();
        await AddLoadedTmdbInfosAsync([.. Enumerable.Range(1, 1201).Select(id => ((long)id, $"Movie {id}"))]);
        var upstreamItems = new JsonArray([.. Enumerable.Range(1, 1201).Select(id => (JsonNode)new JsonObject
        {
            ["stream_id"] = id,
            ["name"] = $"Movie {id}",
            ["category_id"] = "12",
            ["tmdb_id"] = id.ToString()
        })]);
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_streams"] = (HttpStatusCode.OK, upstreamItems.ToJsonString()) });
        var context = CreateContext("?action=get_vod_streams");

        await CreateEndpoint(httpClientFactory).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(1, 1201).Select(id => $"Movie {id}"), ReadResponseItems(context).Select(item => item["name"]?.GetValue<string>()));
    }

    // categories 10 and 11 share a custom category, category 12 is exposed with its original name
    private async Task<(CustomCategory CustomCategory, XtreamCategory Drama)> SeedAsync()
    {
        var customCategory = new CustomCategory { Name = "Movies", ContentType = ContentType.Vod };
        var drama = new XtreamCategory { XtreamId = "12", Name = "Drama", ContentType = ContentType.Vod };
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        source.XtreamCategories.AddRange(
        [
            new XtreamCategory { XtreamId = "10", Name = "Action", ContentType = ContentType.Vod, CustomCategory = customCategory },
            new XtreamCategory { XtreamId = "11", Name = "Comedy", ContentType = ContentType.Vod, CustomCategory = customCategory },
            drama
        ]);
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return (customCategory, drama);
    }

    // loaded metadata without release date: the enriched name is the TMDB title alone
    private async Task AddLoadedTmdbInfosAsync(params (long TmdbId, string Title)[] infos)
    {
        _dbContext.TmdbInfos.AddRange(infos.Select(info => new TmdbInfo
        {
            TmdbId = info.TmdbId,
            ContentType = ContentType.Vod,
            Title = info.Title,
            LoadedAtUtc = DateTimeOffset.UtcNow,
            NextLoadAtUtc = DateTimeOffset.UtcNow.AddDays(60)
        }));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private ItemsGetEndpoint CreateEndpoint(StubXtreamHttpClientFactory httpClientFactory)
        => new(
            httpClientFactory,
            new SourceService(_dbContext, TimeProvider.System),
            new CategoryService(_dbContext),
            new ItemService(new TmdbIdRetrieverQueue(), _tmdbInfoQueue, Options.Create(new TmdbOptions { ApiKey = "token" }), TimeProvider.System),
            new StubTmdbHttpClientFactory(_ => null).CreateTmdbInfoService(_dbContext, TimeProvider.System),
            NullLogger<ItemsGetEndpoint>.Instance);

    private static StubXtreamHttpClientFactory CreateHttpClientFactory(HttpStatusCode statusCode)
        => new(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_streams"] = (statusCode, UpstreamItems) });

    private static string? GetRequestedCategoryId(Uri uri) => HttpUtility.ParseQueryString(uri.Query)["category_id"];

    private static List<JsonObject> ReadResponseItems(XtreamContext context)
    {
        var body = (MemoryStream)context.Response.Body;
        var items = Assert.IsType<JsonArray>(JsonNode.Parse(body.ToArray()));

        return [.. items.Select(item => Assert.IsType<JsonObject>(item))];
    }

    private static XtreamContext CreateContext(string queryString)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.QueryString = new QueryString(queryString);
        httpContext.Response.Body = new MemoryStream();
        return new XtreamContext("http", "provider.example.com", 8080, "player_api.php", httpContext, RequestAction.GetItems, ContentType.Vod);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
