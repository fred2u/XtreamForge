using System.Text.Json.Nodes;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Catalog;
using XtreamForge.ApiService.Services.TmdbIdRetriever;
using XtreamForge.ApiService.Services.TmdbInfos;
using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.History;
using XtreamForge.Domain.Items;
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

    // two streams per TMDB ID, in different upstream categories
    private const string UpstreamDuplicatedItems = """
        [
          { "stream_id": 1, "name": "Comedy movie", "category_id": "11", "tmdb_id": "102" },
          { "stream_id": 2, "name": "Comedy movie 4K", "category_id": "12", "tmdb_id": "102" },
          { "stream_id": 3, "name": "Action movie", "category_id": "10", "tmdb_id": "101" },
          { "stream_id": 4, "name": "Action movie FR", "category_id": "12", "tmdb_id": "101" }
        ]
        """;

    private readonly XtreamForgeDbContext _dbContext;
    private readonly TmdbInfoQueue _tmdbInfoQueue = new();
    private readonly ProviderTmdbIdQueue _providerTmdbIdQueue = new();
    private readonly Dictionary<string, string> _tmdbResponses = [];
    private readonly StubTmdbHttpClientFactory _tmdb;
    private readonly TmdbIdCache _recommendationCache = new(TimeProvider.System);

    public ItemsGetEndpointTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _tmdb = new StubTmdbHttpClientFactory(_tmdbResponses);
    }

    [Fact]
    public async Task GetAsync_WhenAllCategoriesAreRequested_MovesTheRecommendedMoviesToTheRecommendationsCategory()
    {
        var (customCategory, drama) = await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"), (102, "Comedy movie"), (103, "Drama movie"));
        await RecommendAsync(102);
        var context = CreateContext("?action=get_vod_streams&category_id=ALL");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK)).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(
            [("Action movie", customCategory.Id.ToString()), ("Comedy movie", RecommendationCategoryId), ("Drama movie", drama.Id.ToString())],
            ReadResponseItems(context).Select(item => (item["name"]?.GetValue<string>(), item["category_id"]?.GetValue<string>())));
    }

    [Fact]
    public async Task GetAsync_WhenTheRecommendationsCategoryIsRequested_RequestsAllAndReturnsOnlyTheRecommendedMovies()
    {
        await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"), (102, "Comedy movie"), (103, "Drama movie"));
        await RecommendAsync(102, 103);
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK);
        var context = CreateContext($"?action=get_vod_streams&category_id={RecommendationCategoryId}");

        await CreateEndpoint(httpClientFactory).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal("ALL", GetRequestedCategoryId(Assert.Single(httpClientFactory.RequestedUris)));
        Assert.Equal(
            [("Comedy movie", RecommendationCategoryId), ("Drama movie", RecommendationCategoryId)],
            ReadResponseItems(context).Select(item => (item["name"]?.GetValue<string>(), item["category_id"]?.GetValue<string>())));
    }

    [Fact]
    public async Task GetAsync_WhenAnotherCategoryIsRequested_KeepsTheCategoryOfTheRecommendedMoviesWithoutCallingTmdb()
    {
        var (customCategory, _) = await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"), (102, "Comedy movie"));
        await RecommendAsync(102);
        var context = CreateContext($"?action=get_vod_streams&category_id={customCategory.Id}");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK)).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.All(ReadResponseItems(context), item => Assert.Equal(customCategory.Id.ToString(), item["category_id"]?.GetValue<string>()));
        Assert.Empty(_tmdb.RequestedUris);
    }

    [Fact]
    public async Task GetAsync_WhenAllCategoriesAreRequested_MovesThePopularMoviesToThePopularCategoryAfterTheRecommendations()
    {
        var (_, drama) = await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"), (102, "Comedy movie"), (103, "Drama movie"));
        await RecommendAsync(102);
        SetPopular("movie/popular", 101, 102);
        var context = CreateContext("?action=get_vod_streams");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK)).GetAsync(context, TestContext.Current.CancellationToken);

        // 102 is both recommended and popular: the recommendations come first
        Assert.Equal(
            [("Action movie", PopularCategoryId), ("Comedy movie", RecommendationCategoryId), ("Drama movie", drama.Id.ToString())],
            ReadResponseItems(context).Select(item => (item["name"]?.GetValue<string>(), item["category_id"]?.GetValue<string>())));
    }

    [Fact]
    public async Task GetAsync_WhenThePopularCategoryIsRequested_RequestsAllAndReturnsOnlyThePopularMovies()
    {
        await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"), (102, "Comedy movie"), (103, "Drama movie"));
        await RecommendAsync(102);
        SetPopular("movie/popular", 102, 103);
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK);
        var context = CreateContext($"?action=get_vod_streams&category_id={PopularCategoryId}");

        await CreateEndpoint(httpClientFactory).GetAsync(context, TestContext.Current.CancellationToken);

        // a recommended movie is listed in the popular category too when it is requested
        Assert.Equal("ALL", GetRequestedCategoryId(Assert.Single(httpClientFactory.RequestedUris)));
        Assert.Equal(
            [("Comedy movie", PopularCategoryId), ("Drama movie", PopularCategoryId)],
            ReadResponseItems(context).Select(item => (item["name"]?.GetValue<string>(), item["category_id"]?.GetValue<string>())));
        Assert.Equal(PopularService.PageCount, _tmdb.RequestedUris.Count(uri => StubTmdbHttpClientFactory.GetRelativePath(uri) == "movie/popular"));
    }

    [Fact]
    public async Task GetAsync_ForSeries_MovesThePopularShowsToThePopularCategory()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        var drama = new XtreamCategory { XtreamId = "12", Name = "Drama", ContentType = ContentType.Series };
        source.XtreamCategories.Add(drama);
        _dbContext.XtreamSources.Add(source);
        _dbContext.TmdbInfos.AddRange(
            new TmdbInfo { TmdbId = 201, ContentType = ContentType.Series, Title = "Show A", LoadedAtUtc = DateTimeOffset.UtcNow, NextLoadAtUtc = DateTimeOffset.UtcNow.AddDays(60) },
            new TmdbInfo { TmdbId = 202, ContentType = ContentType.Series, Title = "Show B", LoadedAtUtc = DateTimeOffset.UtcNow, NextLoadAtUtc = DateTimeOffset.UtcNow.AddDays(60) });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        SetPopular("tv/popular", 202);
        const string UpstreamSeries = """
            [
              { "series_id": 1, "name": "Show A", "category_id": "12", "tmdb_id": "201" },
              { "series_id": 2, "name": "Show B", "category_id": "12", "tmdb_id": "202" }
            ]
            """;
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { ["get_series"] = (HttpStatusCode.OK, UpstreamSeries) });
        var context = CreateContext("?action=get_series", ContentType.Series);

        await CreateEndpoint(httpClientFactory).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(
            [("Show A", drama.Id.ToString()), ("Show B", PopularCategoryId)],
            ReadResponseItems(context).Select(item => (item["name"]?.GetValue<string>(), item["category_id"]?.GetValue<string>())));
        Assert.DoesNotContain(_tmdb.RequestedUris, uri => StubTmdbHttpClientFactory.GetRelativePath(uri).EndsWith("/recommendations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenAllCategoriesAreRequested_MovesOnlyTheFirstStreamOfATmdbIdToItsVirtualCategory()
    {
        var (_, drama) = await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"), (102, "Comedy movie"));
        await RecommendAsync(102);
        SetPopular("movie/popular", 101, 102);
        var context = CreateContext("?action=get_vod_streams&category_id=ALL");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, UpstreamDuplicatedItems)).GetAsync(context, TestContext.Current.CancellationToken);

        // the next streams keep their provider category, even when the TMDB ID is in another virtual category
        Assert.Equal(
            [("1", RecommendationCategoryId), ("2", drama.Id.ToString()), ("3", PopularCategoryId), ("4", drama.Id.ToString())],
            ReadResponseItems(context).Select(item => (item["stream_id"]?.ToString(), item["category_id"]?.GetValue<string>())));
    }

    [Fact]
    public async Task GetAsync_WhenAVirtualCategoryIsRequested_ListsOnlyTheFirstStreamOfATmdbId()
    {
        await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"), (102, "Comedy movie"));
        SetPopular("movie/popular", 101, 102);
        var context = CreateContext($"?action=get_vod_streams&category_id={PopularCategoryId}");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, UpstreamDuplicatedItems)).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(
            [("1", PopularCategoryId), ("3", PopularCategoryId)],
            ReadResponseItems(context).Select(item => (item["stream_id"]?.ToString(), item["category_id"]?.GetValue<string>())));
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
            Title = "Steel Siege",
            ReleaseDate = new DateOnly(1988, 7, 15),
            LoadedAtUtc = DateTimeOffset.UtcNow,
            NextLoadAtUtc = DateTimeOffset.UtcNow.AddDays(60)
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var context = CreateContext("?action=get_vod_streams");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK)).GetAsync(context, TestContext.Current.CancellationToken);

        // the other items are returned by a later request, once their TMDB metadata is loaded
        Assert.Equal(["Steel Siege | 1988"], ReadResponseItems(context).Select(item => item["name"]?.GetValue<string>()));
        Assert.Equal(2, _tmdbInfoQueue.Count);
    }

    [Fact]
    public async Task GetAsync_EnqueuesTheProviderTmdbIdsOfTheStreamsWithoutMappingByBatch()
    {
        await SeedAsync();
        var sourceId = await _dbContext.XtreamSources.Select(source => source.Id).SingleAsync(TestContext.Current.CancellationToken);
        _dbContext.StreamTmdbMappings.Add(new StreamTmdbMapping { XtreamSourceId = sourceId, ContentType = ContentType.Vod, StreamId = "2", TmdbId = 999 });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        var context = CreateContext("?action=get_vod_streams");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK)).GetAsync(context, TestContext.Current.CancellationToken);

        // the mapped stream 2 keeps its persisted TMDB ID
        await using var requests = _providerTmdbIdQueue.ReadAllAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await requests.MoveNextAsync());
        Assert.Equal(sourceId, requests.Current.XtreamSourceId);
        Assert.Equal(ContentType.Vod, requests.Current.Type);
        Assert.Equal(new Dictionary<string, long> { ["1"] = 101, ["3"] = 103 }, requests.Current.TmdbIds);
        Assert.Equal(0, _providerTmdbIdQueue.Count);
    }

    [Fact]
    public async Task GetAsync_WhenAStreamIsMapped_EnrichesItWithTheMappedTmdbIdInsteadOfTheProviderOne()
    {
        await SeedAsync();
        var sourceId = await _dbContext.XtreamSources.Select(source => source.Id).SingleAsync(TestContext.Current.CancellationToken);
        _dbContext.StreamTmdbMappings.Add(new StreamTmdbMapping { XtreamSourceId = sourceId, ContentType = ContentType.Vod, StreamId = "1", TmdbId = 201 });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        await AddLoadedTmdbInfosAsync((101, "Provider movie"), (201, "Corrected movie"));
        var context = CreateContext("?action=get_vod_streams");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK)).GetAsync(context, TestContext.Current.CancellationToken);

        // the provider exposes tmdb_id 101 for stream 1, corrected manually to 201
        Assert.Equal(
            [("1", "201", "Corrected movie")],
            ReadResponseItems(context).Select(item => (item["stream_id"]?.ToString(), item["tmdb_id"]?.ToString(), item["name"]?.GetValue<string>())));
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

    [Fact]
    public async Task GetAsync_WritesTheListAsJson()
    {
        await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"));
        var context = CreateContext("?action=get_vod_streams");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK)).GetAsync(context, TestContext.Current.CancellationToken);

        // the media type lets the response compression apply to the list
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
    }

    [Fact]
    public async Task GetAsync_WhenTheUpstreamListBreaksWhileStreaming_AbortsTheConnection()
    {
        await SeedAsync();
        await AddLoadedTmdbInfosAsync((101, "Action movie"));
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_streams"] = (HttpStatusCode.OK, """[{ "stream_id": 1, "name": "Action movie", "category_id": "10", "tmdb_id": "101" }, { "stream_id": """)
        });
        var httpContext = new ServerLikeHttpContext();
        httpContext.HttpContext.Request.Method = HttpMethods.Get;
        httpContext.HttpContext.Request.QueryString = new QueryString("?action=get_vod_streams");
        var context = new XtreamContext("http", "provider.example.com", 8080, "player_api.php", httpContext.HttpContext, RequestAction.GetItems, ContentType.Vod);

        var result = await CreateEndpoint(httpClientFactory).GetAsync(context, TestContext.Current.CancellationToken);

        // the status is already sent: the truncated list must not look complete to the client
        Assert.IsType<EmptyHttpResult>(result);
        Assert.True(httpContext.IsAborted);
        Assert.Equal(StatusCodes.Status200OK, httpContext.HttpContext.Response.StatusCode);
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

    // the watched movie 999 is recommended the given movies by TMDB
    private async Task RecommendAsync(params long[] tmdbIds)
    {
        _dbContext.WatchHistory.Add(new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 999, StartedAtUtc = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        _tmdbResponses["movie/999/recommendations"] = $$"""{ "results": [{{string.Join(", ", tmdbIds.Select(tmdbId => $$"""{ "id": {{tmdbId}} }"""))}}] }""";
    }

    private void SetPopular(string path, params long[] tmdbIds)
        => _tmdbResponses[path] = $$"""{ "results": [{{string.Join(", ", tmdbIds.Select(tmdbId => $$"""{ "id": {{tmdbId}} }"""))}}] }""";

    private static string RecommendationCategoryId => new RecommendationOptions().CategoryId.ToString();

    private static string PopularCategoryId => new PopularOptions().CategoryId.ToString();

    private ItemsGetEndpoint CreateEndpoint(StubXtreamHttpClientFactory httpClientFactory)
        => new(
            httpClientFactory,
            new SourceService(_dbContext, TimeProvider.System),
            new CategoryService(_dbContext, TimeProvider.System),
            new ItemService(new TmdbIdRetrieverQueue(), _providerTmdbIdQueue, _tmdbInfoQueue, Options.Create(new TmdbOptions { ApiKey = "token" }), TimeProvider.System),
            new StubTmdbHttpClientFactory(_ => null).CreateTmdbInfoService(_dbContext, TimeProvider.System),
            _tmdb.CreateVirtualCategoryService(_dbContext, _recommendationCache),
            NullLogger<ItemsGetEndpoint>.Instance);

    private static StubXtreamHttpClientFactory CreateHttpClientFactory(HttpStatusCode statusCode, string upstreamItems = UpstreamItems)
        => new(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_streams"] = (statusCode, upstreamItems) });

    private static string? GetRequestedCategoryId(Uri uri) => HttpUtility.ParseQueryString(uri.Query)["category_id"];

    private static List<JsonObject> ReadResponseItems(XtreamContext context)
    {
        var body = (MemoryStream)context.Response.Body;
        var items = Assert.IsType<JsonArray>(JsonNode.Parse(body.ToArray()));

        return [.. items.Select(item => Assert.IsType<JsonObject>(item))];
    }

    private static XtreamContext CreateContext(string queryString, ContentType contentType = ContentType.Vod)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.QueryString = new QueryString(queryString);
        httpContext.Response.Body = new MemoryStream();
        return new XtreamContext("http", "provider.example.com", 8080, "player_api.php", httpContext, RequestAction.GetItems, contentType);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _recommendationCache.Dispose();
        GC.SuppressFinalize(this);
    }
}
