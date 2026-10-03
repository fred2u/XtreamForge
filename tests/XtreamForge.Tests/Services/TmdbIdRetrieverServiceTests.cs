using System.Net;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;
using XtreamForge.Tests.Services.Tmdb;

namespace XtreamForge.Tests.Services;

public class TmdbIdRetrieverServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly SteppingTimeProvider _time = new();
    private readonly TmdbInfoQueue _tmdbInfoQueue = new();

    public TmdbIdRetrieverServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Theory]
    [InlineData(ContentType.Vod, "get_vod_info", """{ "info": { "tmdb_id": "603" } }""")]
    [InlineData(ContentType.Vod, "get_vod_info", """{ "info": { "tmdb_id": 603 } }""")]
    [InlineData(ContentType.Vod, "get_vod_info", """{ "info": { "tmdb_id": "" }, "movie_data": { "tmdb_id": "603" } }""")]
    [InlineData(ContentType.Vod, "get_vod_info", """{ "info": [], "movie_data": { "tmdb_id": 603 } }""")]
    [InlineData(ContentType.Series, "get_series_info", """{ "info": { "tmdb_id": "603" } }""")]
    public async Task RetrieveAsync_WhenProviderReturnsTmdbId_PersistsMapping(ContentType type, string action, string content)
    {
        var source = await CreateSourceAsync();
        var service = CreateService(new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { [action] = (HttpStatusCode.OK, content) }));

        await service.RetrieveAsync(CreateRequest(source.Id, type, "42"), TestContext.Current.CancellationToken);

        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(source.Id, mapping.XtreamSourceId);
        Assert.Equal(type, mapping.ContentType);
        Assert.Equal("42", mapping.StreamId);
        Assert.Equal(603, mapping.TmdbId);
    }

    [Theory]
    [InlineData(ContentType.Vod, "https://provider.example.com:8443/player_api.php?username=us%20er&password=p%26ss&action=get_vod_info&vod_id=42")]
    [InlineData(ContentType.Series, "https://provider.example.com:8443/player_api.php?username=us%20er&password=p%26ss&action=get_series_info&series_id=42")]
    public async Task RetrieveAsync_CallsInfoActionWithCredentials(ContentType type, string expectedUri)
    {
        var source = await CreateSourceAsync();
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_info"] = (HttpStatusCode.OK, "{}"),
            ["get_series_info"] = (HttpStatusCode.OK, "{}")
        });
        var service = CreateService(httpClientFactory);

        await service.RetrieveAsync(CreateRequest(source.Id, type, "42"), TestContext.Current.CancellationToken);

        Assert.Equal(expectedUri, Assert.Single(httpClientFactory.RequestedUris).AbsoluteUri);
    }

    [Theory]
    [InlineData("""{ "info": { "tmdb_id": "" } }""")]
    [InlineData("""{ "info": { "tmdb_id": null } }""")]
    [InlineData("""{ "info": { "tmdb_id": "0" } }""")]
    [InlineData("""{ "info": { "tmdb_id": "abc" } }""")]
    [InlineData("""{ "info": {} }""")]
    [InlineData("""[]""")]
    public async Task RetrieveAsync_WhenTmdbIdIsMissingOrInvalid_DefersNextLookupByOneDay(string content)
    {
        var source = await CreateSourceAsync();
        var service = CreateService(new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_info"] = (HttpStatusCode.OK, content) }));

        var found = await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Vod, "42"), TestContext.Current.CancellationToken);

        Assert.False(found);
        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(source.Id, mapping.XtreamSourceId);
        Assert.Equal("42", mapping.StreamId);
        Assert.Null(mapping.TmdbId);
        Assert.Equal(1, mapping.LookupAttemptCount);
        Assert.Equal(_time.Now.AddDays(1), mapping.NextLookupAtUtc);
    }

    [Fact]
    public async Task RetrieveAsync_ForSeries_IgnoresMovieDataSection()
    {
        var source = await CreateSourceAsync();
        var service = CreateService(new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_series_info"] = (HttpStatusCode.OK, """{ "info": {}, "movie_data": { "tmdb_id": "603" } }""")
        }));

        await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Series, "42"), TestContext.Current.CancellationToken);

        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(mapping.TmdbId);
    }

    [Fact]
    public async Task RetrieveAsync_WhenMappingAlreadyExists_DoesNotCallProvider()
    {
        var source = await CreateSourceAsync();
        _dbContext.StreamTmdbMappings.Add(new StreamTmdbMapping { XtreamSourceId = source.Id, ContentType = ContentType.Vod, StreamId = "42", TmdbId = 1 });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>());
        var service = CreateService(httpClientFactory);

        await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Vod, "42"), TestContext.Current.CancellationToken);

        Assert.Empty(httpClientFactory.RequestedUris);
        Assert.Equal(1, await _dbContext.StreamTmdbMappings.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetrieveAsync_WhenProviderFails_ThrowsAndDefersNextLookup()
    {
        var source = await CreateSourceAsync();
        var service = CreateService(new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_info"] = (HttpStatusCode.Unauthorized, string.Empty) }));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.RetrieveAsync(CreateRequest(source.Id, ContentType.Vod, "42"), TestContext.Current.CancellationToken));

        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(mapping.TmdbId);
        Assert.Equal(1, mapping.LookupAttemptCount);
        Assert.Equal(_time.Now.AddDays(1), mapping.NextLookupAtUtc);
    }

    [Fact]
    public async Task RetrieveAsync_WhenNextLookupIsNotDue_DoesNotCallProvider()
    {
        var source = await CreateSourceAsync();
        await AddPendingMappingAsync(source.Id, attemptCount: 1, _time.Now.AddMinutes(1));
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>());
        var service = CreateService(httpClientFactory);

        var found = await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Vod, "42"), TestContext.Current.CancellationToken);

        Assert.False(found);
        Assert.Empty(httpClientFactory.RequestedUris);
        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, mapping.LookupAttemptCount);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]
    [InlineData(50, 30)]
    public async Task RetrieveAsync_WhenDueLookupFindsNothingAgain_DoublesDelayUpToThirtyDays(int previousAttemptCount, int expectedDelayInDays)
    {
        var source = await CreateSourceAsync();
        await AddPendingMappingAsync(source.Id, previousAttemptCount, _time.Now);
        var service = CreateService(new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_info"] = (HttpStatusCode.OK, "{}") }));

        await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Vod, "42"), TestContext.Current.CancellationToken);

        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(previousAttemptCount + 1, mapping.LookupAttemptCount);
        Assert.Equal(_time.Now.AddDays(expectedDelayInDays), mapping.NextLookupAtUtc);
    }

    [Fact]
    public async Task RetrieveAsync_WhenDueLookupFindsTmdbId_StoresItAndClearsNextLookup()
    {
        var source = await CreateSourceAsync();
        await AddPendingMappingAsync(source.Id, attemptCount: 2, _time.Now.AddMinutes(-1));
        var service = CreateService(new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_info"] = (HttpStatusCode.OK, """{ "info": { "tmdb_id": "603" } }""") }));

        var found = await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Vod, "42"), TestContext.Current.CancellationToken);

        Assert.True(found);
        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(603, mapping.TmdbId);
        Assert.Null(mapping.NextLookupAtUtc);
    }

    [Fact]
    public void TmdbIdRetrieverRequest_ToString_DoesNotExposeCredentials()
    {
        var text = CreateRequest(1, ContentType.Vod, "42").ToString();

        Assert.DoesNotContain("us er", text, StringComparison.Ordinal);
        Assert.DoesNotContain("p&ss", text, StringComparison.Ordinal);
        Assert.Contains("42", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrieveAsync_WhenProviderHasNoTmdbId_UsesTmdbSearchAndPersistsMatch()
    {
        var source = await CreateSourceAsync();
        var providerFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_info"] = (HttpStatusCode.OK, TmdbTestData.MatrixProviderInfo)
        });
        var tmdbFactory = new StubTmdbHttpClientFactory(new Dictionary<string, string>
        {
            ["search/movie"] = """{ "results": [{ "id": 603 }] }""",
            ["movie/603"] = TmdbTestData.MatrixDetails
        });
        var service = CreateService(providerFactory, tmdbFactory, "token");

        await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Vod, "42"), TestContext.Current.CancellationToken);

        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(603, mapping.TmdbId);
    }

    [Fact]
    public async Task RetrieveAsync_WhenProviderHasTmdbId_EnqueuesTheLoadOfItsMetadata()
    {
        var source = await CreateSourceAsync();
        var service = CreateService(new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { ["get_series_info"] = (HttpStatusCode.OK, """{ "info": { "tmdb_id": "1399" } }""") }));

        await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Series, "42"), TestContext.Current.CancellationToken);

        Assert.False(_tmdbInfoQueue.TryEnqueue(new TmdbInfoRequest(ContentType.Series, 1399)));
        Assert.Equal(1, _tmdbInfoQueue.Count);
    }

    [Fact]
    public async Task RetrieveAsync_WhenTmdbSearchFindsTheTmdbId_EnqueuesTheLoadOfItsMetadataWithoutStoringIt()
    {
        var source = await CreateSourceAsync();
        var providerFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_info"] = (HttpStatusCode.OK, TmdbTestData.MatrixProviderInfo)
        });
        var tmdbFactory = new StubTmdbHttpClientFactory(new Dictionary<string, string>
        {
            ["search/movie"] = """{ "results": [{ "id": 603, "title": "Matrix" }, { "id": 604, "title": "Matrix Reloaded" }] }""",
            ["movie/603"] = TmdbTestData.MatrixDetails,
            ["movie/604"] = """{ "id": 604, "title": "Matrix Reloaded", "release_date": "2003-05-15" }"""
        });
        var service = CreateService(providerFactory, tmdbFactory, "token");

        await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Vod, "42"), TestContext.Current.CancellationToken);

        Assert.False(_tmdbInfoQueue.TryEnqueue(new TmdbInfoRequest(ContentType.Vod, 603)));
        Assert.Equal(1, _tmdbInfoQueue.Count);
        // the search results are not stored as TMDB metadata: the metadata is loaded in the background
        Assert.False(await _dbContext.TmdbInfos.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetrieveAsync_WhenNoTmdbIdIsFound_EnqueuesNothing()
    {
        var source = await CreateSourceAsync();
        var service = CreateService(new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_info"] = (HttpStatusCode.OK, "{}") }));

        await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Vod, "42"), TestContext.Current.CancellationToken);

        Assert.Equal(0, _tmdbInfoQueue.Count);
    }

    [Fact]
    public async Task RetrieveAsync_WhenProviderHasTmdbId_DoesNotCallTmdb()
    {
        var source = await CreateSourceAsync();
        var providerFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_info"] = (HttpStatusCode.OK, """{ "info": { "tmdb_id": "603" } }""")
        });
        var tmdbFactory = new StubTmdbHttpClientFactory(_ => null);
        var service = CreateService(providerFactory, tmdbFactory, "token");

        await service.RetrieveAsync(CreateRequest(source.Id, ContentType.Vod, "42"), TestContext.Current.CancellationToken);

        Assert.Empty(tmdbFactory.RequestedUris);
    }

    private async Task<XtreamSource> CreateSourceAsync()
    {
        var source = new XtreamSource { Protocol = "https", Host = "provider.example.com", Port = 8443 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return source;
    }

    private async Task AddPendingMappingAsync(int xtreamSourceId, int attemptCount, DateTimeOffset nextLookupAtUtc)
    {
        _dbContext.StreamTmdbMappings.Add(new StreamTmdbMapping
        {
            XtreamSourceId = xtreamSourceId,
            ContentType = ContentType.Vod,
            StreamId = "42",
            LookupAttemptCount = attemptCount,
            NextLookupAtUtc = nextLookupAtUtc
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private static TmdbIdRetrieverRequest CreateRequest(int xtreamSourceId, ContentType type, string streamId) =>
        new(xtreamSourceId, "https", "provider.example.com", 8443, "us er", "p&ss", streamId, type);

    // without an api key the TMDB fallback is skipped
    private TmdbIdRetrieverService CreateService(StubXtreamHttpClientFactory httpClientFactory, StubTmdbHttpClientFactory? tmdbHttpClientFactory = null, string tmdbApiKey = "")
    {
        var tmdbFactory = tmdbHttpClientFactory ?? new StubTmdbHttpClientFactory(_ => null);

        return new(httpClientFactory, _dbContext, tmdbFactory.CreateMatcher(tmdbApiKey), _tmdbInfoQueue, _time);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
