using System.Text.Json.Nodes;
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
using XtreamForge.ApiService.Services.WatchHistory;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints;

public class ItemGetEndpointTests : IAsyncDisposable
{
    private const string UpstreamVodInfo = """
        { "info": { "name": "Action movie" }, "movie_data": { "stream_id": 1, "name": "Action movie", "category_id": "10" } }
        """;

    private const string UpstreamSeriesInfo = """
        { "seasons": [], "info": { "name": "Action show", "category_id": "10" }, "episodes": {} }
        """;

    private const string UpstreamSeriesInfoWithEpisodes = """
        { "seasons": [], "info": { "name": "Action show", "category_id": "10" },
          "episodes": { "1": [ { "id": "1001", "episode_num": 1, "season": 1 }, { "id": "1002", "episode_num": 2, "season": 1 } ] } }
        """;

    private readonly XtreamForgeDbContext _dbContext;
    private readonly SeriesEpisodeQueue _seriesEpisodeQueue = new();

    public ItemGetEndpointTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Fact]
    public async Task GetAsync_WhenSourceIsUnknown_ReturnsBadRequestWithoutCallingUpstream()
    {
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK, UpstreamVodInfo);

        var result = await CreateEndpoint(httpClientFactory).GetAsync(CreateContext(ContentType.Vod, "?action=get_vod_info&vod_id=1"), TestContext.Current.CancellationToken);

        Assert.IsType<BadRequest<string>>(result);
        Assert.Empty(httpClientFactory.RequestedUris);
    }

    [Theory]
    [InlineData(ContentType.Vod, "?action=get_vod_info")]
    [InlineData(ContentType.Vod, "?action=get_vod_info&series_id=1")]
    [InlineData(ContentType.Series, "?action=get_series_info&series_id=%20")]
    public async Task GetAsync_WhenItemIdIsMissing_ReturnsBadRequestWithoutCallingUpstream(ContentType contentType, string queryString)
    {
        await SeedAsync(contentType, tmdbId: 603);
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK, UpstreamVodInfo);

        var result = await CreateEndpoint(httpClientFactory).GetAsync(CreateContext(contentType, queryString), TestContext.Current.CancellationToken);

        Assert.IsType<BadRequest<string>>(result);
        Assert.Empty(httpClientFactory.RequestedUris);
    }

    [Fact]
    public async Task GetAsync_ForVod_ForwardsRequestAndRewritesCategoryAndTmdbId()
    {
        var category = await SeedAsync(ContentType.Vod, tmdbId: 603);
        await AddLoadedTmdbInfoAsync(ContentType.Vod, 603, "Action movie");
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK, UpstreamVodInfo);
        var context = CreateContext(ContentType.Vod, "?username=user&password=secret&action=get_vod_info&vod_id=1");

        await CreateEndpoint(httpClientFactory).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(
            "http://provider.example.com:8080/player_api.php?username=user&password=secret&action=get_vod_info&vod_id=1",
            Assert.Single(httpClientFactory.RequestedUris).AbsoluteUri);
        var payload = ReadResponse(context);
        Assert.Equal(category.Id.ToString(), payload["movie_data"]?["category_id"]?.GetValue<string>());
        Assert.Equal("603", payload["info"]?["tmdb_id"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetAsync_ForSeries_RewritesInfoSection()
    {
        var category = await SeedAsync(ContentType.Series, tmdbId: 1399);
        await AddLoadedTmdbInfoAsync(ContentType.Series, 1399, "Action show");
        var context = CreateContext(ContentType.Series, "?action=get_series_info&series_id=1");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, UpstreamSeriesInfo, "get_series_info")).GetAsync(context, TestContext.Current.CancellationToken);

        var payload = ReadResponse(context);
        Assert.Equal(category.Id.ToString(), payload["info"]?["category_id"]?.GetValue<string>());
        Assert.Equal("1399", payload["info"]?["tmdb_id"]?.GetValue<string>());
        Assert.NotNull(payload["episodes"]);
    }

    [Fact]
    public async Task GetAsync_ForSeries_EnqueuesTheEpisodesForTheWatchHistory()
    {
        await SeedAsync(ContentType.Series, tmdbId: 1399);
        var context = CreateContext(ContentType.Series, "?action=get_series_info&series_id=1");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, UpstreamSeriesInfoWithEpisodes, "get_series_info")).GetAsync(context, TestContext.Current.CancellationToken);

        var source = await _dbContext.XtreamSources.SingleAsync(TestContext.Current.CancellationToken);
        var request = await ReadSeriesEpisodeRequestAsync();
        Assert.Equal((source.Id, "1"), (request.XtreamSourceId, request.SeriesId));
        Assert.Equal([new XtreamEpisode("1001", 1, 1), new XtreamEpisode("1002", 1, 2)], request.Episodes);
    }

    [Fact]
    public async Task GetAsync_ForSeries_WhenTheSeriesWouldNotBeListed_StillEnqueuesTheEpisodes()
    {
        // the TMDB metadata of the series is not loaded: the client receives an empty payload
        await SeedAsync(ContentType.Series, tmdbId: 1399);
        var context = CreateContext(ContentType.Series, "?action=get_series_info&series_id=1");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, UpstreamSeriesInfoWithEpisodes, "get_series_info")).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal("""{"seasons":[],"info":[],"episodes":[]}""", ReadResponse(context).ToJsonString());
        Assert.Equal(1, _seriesEpisodeQueue.Count);
    }

    [Fact]
    public async Task GetAsync_ForVod_DoesNotEnqueueEpisodes()
    {
        await SeedAsync(ContentType.Vod, tmdbId: 603);
        await AddLoadedTmdbInfoAsync(ContentType.Vod, 603, "Action movie");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, UpstreamVodInfo))
            .GetAsync(CreateContext(ContentType.Vod, "?action=get_vod_info&vod_id=1"), TestContext.Current.CancellationToken);

        Assert.Equal(0, _seriesEpisodeQueue.Count);
    }

    [Fact]
    public async Task GetAsync_ForVod_EnrichesWithLoadedTmdbInfo()
    {
        await SeedAsync(ContentType.Vod, tmdbId: 603);
        await AddLoadedTmdbInfoAsync(ContentType.Vod, 603, "Lattice", new DateOnly(1999, 3, 30));
        var context = CreateContext(ContentType.Vod, "?action=get_vod_info&vod_id=1");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, UpstreamVodInfo)).GetAsync(context, TestContext.Current.CancellationToken);

        var payload = ReadResponse(context);
        Assert.Equal("Lattice | 1999", payload["info"]?["name"]?.GetValue<string>());
        Assert.Equal("Lattice | 1999", payload["movie_data"]?["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetAsync_ForVod_WhenItemWouldNotBeListed_ReturnsEmptyXtreamPayload()
    {
        // no TMDB ID is known for the item
        await SeedAsync(ContentType.Vod, tmdbId: null);
        var context = CreateContext(ContentType.Vod, "?action=get_vod_info&vod_id=1");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, UpstreamVodInfo)).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("""{"info":[],"movie_data":[]}""", ReadResponse(context).ToJsonString());
    }

    [Fact]
    public async Task GetAsync_ForVod_WhenTmdbInfoIsNotLoadedYet_ReturnsEmptyXtreamPayload()
    {
        // the TMDB ID is known, but its metadata is not loaded yet
        await SeedAsync(ContentType.Vod, tmdbId: 603);
        var context = CreateContext(ContentType.Vod, "?action=get_vod_info&vod_id=1");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, UpstreamVodInfo)).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal("""{"info":[],"movie_data":[]}""", ReadResponse(context).ToJsonString());
    }

    [Fact]
    public async Task GetAsync_ForSeries_WhenNoCategoryIsIncluded_ReturnsEmptyXtreamPayloadWithoutCallingUpstream()
    {
        await SeedAsync(ContentType.Vod, tmdbId: 603);
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK, UpstreamSeriesInfo, "get_series_info");
        var context = CreateContext(ContentType.Series, "?action=get_series_info&series_id=1");

        // the source only has VOD categories
        await CreateEndpoint(httpClientFactory).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(httpClientFactory.RequestedUris);
        Assert.Equal("""{"seasons":[],"info":[],"episodes":[]}""", ReadResponse(context).ToJsonString());
    }

    [Fact]
    public async Task GetAsync_WhenUpstreamFails_ForwardsUpstreamStatus()
    {
        await SeedAsync(ContentType.Vod, tmdbId: 603);
        var context = CreateContext(ContentType.Vod, "?action=get_vod_info&vod_id=1");

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.Unauthorized, string.Empty)).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task GetAsync_WhenUpstreamReturnsInvalidJson_ReturnsBadGateway()
    {
        await SeedAsync(ContentType.Vod, tmdbId: 603);

        var result = await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, "not json"))
            .GetAsync(CreateContext(ContentType.Vod, "?action=get_vod_info&vod_id=1"), TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<StatusCodeHttpResult>(result).StatusCode);
    }

    // upstream category 10 of the requested content type, and the TMDB mapping of item 1 when known
    private async Task<XtreamCategory> SeedAsync(ContentType contentType, long? tmdbId)
    {
        var category = new XtreamCategory { XtreamId = "10", Name = "Action", ContentType = contentType };
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        source.XtreamCategories.Add(category);
        if (tmdbId is not null)
            source.StreamTmdbMappings.Add(new StreamTmdbMapping { ContentType = contentType, StreamId = "1", TmdbId = tmdbId });

        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return category;
    }

    private async Task AddLoadedTmdbInfoAsync(ContentType contentType, long tmdbId, string title, DateOnly? releaseDate = null)
    {
        _dbContext.TmdbInfos.Add(new TmdbInfo
        {
            TmdbId = tmdbId,
            ContentType = contentType,
            Title = title,
            ReleaseDate = releaseDate,
            LoadedAtUtc = DateTimeOffset.UtcNow,
            NextLoadAtUtc = DateTimeOffset.UtcNow.AddDays(60)
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private ItemGetEndpoint CreateEndpoint(StubXtreamHttpClientFactory httpClientFactory)
        => new(
            httpClientFactory,
            new SourceService(_dbContext, TimeProvider.System),
            new CategoryService(_dbContext, TimeProvider.System),
            new ItemService(new TmdbIdRetrieverQueue(), new ProviderTmdbIdQueue(), new TmdbInfoQueue(), Options.Create(new TmdbOptions { ApiKey = "token" }), TimeProvider.System),
            new StubTmdbHttpClientFactory(_ => null).CreateTmdbInfoService(_dbContext, TimeProvider.System),
            _seriesEpisodeQueue,
            NullLogger<ItemGetEndpoint>.Instance);

    private async Task<SeriesEpisodeRequest> ReadSeriesEpisodeRequestAsync()
    {
        await using var requests = _seriesEpisodeQueue.ReadAllAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await requests.MoveNextAsync());
        return requests.Current;
    }

    private static StubXtreamHttpClientFactory CreateHttpClientFactory(HttpStatusCode statusCode, string content, string action = "get_vod_info")
        => new(new Dictionary<string, (HttpStatusCode, string)> { [action] = (statusCode, content) });

    private static JsonObject ReadResponse(XtreamContext context)
    {
        var body = (MemoryStream)context.Response.Body;

        return Assert.IsType<JsonObject>(JsonNode.Parse(body.ToArray()));
    }

    private static XtreamContext CreateContext(ContentType contentType, string queryString)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.QueryString = new QueryString(queryString);
        httpContext.Response.Body = new MemoryStream();
        return new XtreamContext("http", "provider.example.com", 8080, "player_api.php", httpContext, RequestAction.GetInfo, contentType);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
