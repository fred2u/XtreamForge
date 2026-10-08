using System.Net;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.ApiService.Services.WatchHistory;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.WatchHistory;

public class WatchHistoryServiceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 10, 1, 20, 30, 0, TimeSpan.Zero);

    private readonly XtreamForgeDbContext _dbContext;
    private readonly TmdbIdCache _recommendationCache = new(TimeProvider.System);

    public WatchHistoryServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Theory]
    [InlineData(ContentType.Vod, ContentType.Series)]
    [InlineData(ContentType.Series, ContentType.Vod)]
    public async Task RecordAsync_InvalidatesOnlyTheCachedRecommendationsOfTheContentType(ContentType contentType, ContentType otherContentType)
    {
        var source = await CreateSourceAsync();
        await AddMappingAsync(source.Id, contentType, 603);
        await AddEpisodeAsync(source.Id, "1001", seriesId: "42", seasonNumber: 1, episodeNumber: 1);
        await ComputesRecommendationsAsync(ContentType.Vod);
        await ComputesRecommendationsAsync(ContentType.Series);

        var request = contentType == ContentType.Vod ? CreateRequest() : CreateEpisodeRequest("1001");
        await new WatchHistoryService(Provider("{}"), _dbContext, _recommendationCache).RecordAsync(request, TestContext.Current.CancellationToken);

        Assert.True(await ComputesRecommendationsAsync(contentType));
        Assert.False(await ComputesRecommendationsAsync(otherContentType));
    }

    [Fact]
    public async Task RecordAsync_WhenTheMovieIsMapped_RecordsTheMappedTmdbIdWithoutCallingTheProvider()
    {
        var source = await CreateSourceAsync();
        await AddMappingAsync(source.Id, ContentType.Vod, 603);
        var httpClientFactory = Provider("""{ "info": { "tmdb_id": "999" } }""");

        var recorded = await new WatchHistoryService(httpClientFactory, _dbContext, _recommendationCache).RecordAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.True(recorded);
        var entry = await _dbContext.WatchHistory.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            ((int?)source.Id, ContentType.Vod, 603L, StartedAtUtc, (int?)null, (int?)null),
            (entry.XtreamSourceId, entry.ContentType, entry.TmdbId, entry.StartedAtUtc, entry.SeasonNumber, entry.EpisodeNumber));
        Assert.Empty(httpClientFactory.RequestedUris);
    }

    [Fact]
    public async Task RecordAsync_WhenTheMovieIsNotMapped_RecordsTheProviderTmdbId()
    {
        var source = await CreateSourceAsync();
        var httpClientFactory = Provider("""{ "info": [], "movie_data": { "tmdb_id": 603 } }""");

        var recorded = await new WatchHistoryService(httpClientFactory, _dbContext, _recommendationCache).RecordAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.True(recorded);
        var entry = await _dbContext.WatchHistory.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(((int?)source.Id, 603L), (entry.XtreamSourceId, entry.TmdbId));
        Assert.Equal(
            "https://provider.example.com:8443/player_api.php?username=us%20er&password=p%26ss&action=get_vod_info&vod_id=42",
            Assert.Single(httpClientFactory.RequestedUris).AbsoluteUri);
    }

    [Theory]
    [InlineData(ContentType.Series, 1399L)]
    [InlineData(ContentType.Vod, null)]
    public async Task RecordAsync_IgnoresTheMappingsOfSeriesAndUnresolvedMappings(ContentType contentType, long? tmdbId)
    {
        var source = await CreateSourceAsync();
        await AddMappingAsync(source.Id, contentType, tmdbId);
        var httpClientFactory = Provider("""{ "info": { "tmdb_id": "603" } }""");

        await new WatchHistoryService(httpClientFactory, _dbContext, _recommendationCache).RecordAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(603, (await _dbContext.WatchHistory.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).TmdbId);
    }

    [Theory]
    [InlineData("""{ "info": { "tmdb_id": "" } }""")]
    [InlineData("""{ "info": [], "movie_data": [] }""")]
    [InlineData("""[]""")]
    public async Task RecordAsync_WithoutTmdbId_RecordsNothing(string providerInfo)
    {
        await CreateSourceAsync();

        var recorded = await new WatchHistoryService(Provider(providerInfo), _dbContext, _recommendationCache).RecordAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.False(recorded);
        Assert.False(await _dbContext.WatchHistory.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordAsync_ForAnUnknownSource_RecordsNothingWithoutCallingTheProvider()
    {
        var httpClientFactory = Provider("""{ "info": { "tmdb_id": "603" } }""");

        var recorded = await new WatchHistoryService(httpClientFactory, _dbContext, _recommendationCache).RecordAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.False(recorded);
        Assert.Empty(httpClientFactory.RequestedUris);
    }

    [Fact]
    public async Task RecordAsync_WhenTheProviderFails_Throws()
    {
        await CreateSourceAsync();
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_info"] = (HttpStatusCode.BadGateway, string.Empty) });

        await Assert.ThrowsAsync<HttpRequestException>(() => new WatchHistoryService(httpClientFactory, _dbContext, _recommendationCache).RecordAsync(CreateRequest(), TestContext.Current.CancellationToken));
        Assert.False(await _dbContext.WatchHistory.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordAsync_KeepsOneEntryPerPlayback()
    {
        var source = await CreateSourceAsync();
        await AddMappingAsync(source.Id, ContentType.Vod, 603);
        var service = new WatchHistoryService(Provider("{}"), _dbContext, _recommendationCache);

        await service.RecordAsync(CreateRequest(), TestContext.Current.CancellationToken);
        await service.RecordAsync(CreateRequest() with { StartedAtUtc = StartedAtUtc.AddDays(1) }, TestContext.Current.CancellationToken);

        Assert.Equal(
            [StartedAtUtc, StartedAtUtc.AddDays(1)],
            await _dbContext.WatchHistory.Where(entry => entry.TmdbId == 603).OrderBy(entry => entry.Id).Select(entry => entry.StartedAtUtc).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordAsync_ForAListedEpisodeOfAMappedSeries_RecordsTheSeriesTmdbIdWithTheSeasonAndEpisodeWithoutCallingTheProvider()
    {
        var source = await CreateSourceAsync();
        await AddMappingAsync(source.Id, ContentType.Series, 1399);
        await AddEpisodeAsync(source.Id, "1001", seriesId: "42", seasonNumber: 2, episodeNumber: 5);
        var httpClientFactory = Provider("{}");

        var recorded = await new WatchHistoryService(httpClientFactory, _dbContext, _recommendationCache).RecordAsync(CreateEpisodeRequest("1001"), TestContext.Current.CancellationToken);

        Assert.True(recorded);
        var entry = await _dbContext.WatchHistory.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            ((int?)source.Id, ContentType.Series, 1399L, (int?)2, (int?)5, StartedAtUtc),
            (entry.XtreamSourceId, entry.ContentType, entry.TmdbId, entry.SeasonNumber, entry.EpisodeNumber, entry.StartedAtUtc));
        Assert.Empty(httpClientFactory.RequestedUris);
    }

    [Fact]
    public async Task RecordAsync_ForAListedEpisodeOfAnUnmappedSeries_RecordsTheProviderTmdbIdOfTheSeries()
    {
        var source = await CreateSourceAsync();
        await AddEpisodeAsync(source.Id, "1001", seriesId: "42", seasonNumber: null, episodeNumber: null);
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_series_info"] = (HttpStatusCode.OK, """{ "info": { "tmdb_id": "1399" }, "episodes": {} }""")
        });

        var recorded = await new WatchHistoryService(httpClientFactory, _dbContext, _recommendationCache).RecordAsync(CreateEpisodeRequest("1001"), TestContext.Current.CancellationToken);

        Assert.True(recorded);
        var entry = await _dbContext.WatchHistory.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((ContentType.Series, 1399L, (int?)null, (int?)null), (entry.ContentType, entry.TmdbId, entry.SeasonNumber, entry.EpisodeNumber));
        Assert.Equal(
            "https://provider.example.com:8443/player_api.php?username=us%20er&password=p%26ss&action=get_series_info&series_id=42",
            Assert.Single(httpClientFactory.RequestedUris).AbsoluteUri);
    }

    [Fact]
    public async Task RecordAsync_ForAnEpisodeNotListedForTheSource_RecordsNothingWithoutCallingTheProvider()
    {
        var source = await CreateSourceAsync();
        var otherSource = await CreateSourceAsync("other.example.com");
        await AddMappingAsync(source.Id, ContentType.Series, 1399);
        await AddEpisodeAsync(otherSource.Id, "1001", seriesId: "42", seasonNumber: 1, episodeNumber: 1);
        var httpClientFactory = Provider("{}");

        var recorded = await new WatchHistoryService(httpClientFactory, _dbContext, _recommendationCache).RecordAsync(CreateEpisodeRequest("1001"), TestContext.Current.CancellationToken);

        Assert.False(recorded);
        Assert.False(await _dbContext.WatchHistory.AnyAsync(TestContext.Current.CancellationToken));
        Assert.Empty(httpClientFactory.RequestedUris);
    }

    // reads the cached recommendations of a content type, computed again only when missing or invalidated; returns whether they were computed
    private async Task<bool> ComputesRecommendationsAsync(ContentType contentType)
    {
        var isComputed = false;
        await _recommendationCache.GetOrComputeAsync(
            TmdbIdCache.RecommendationsKey(contentType),
            _ =>
            {
                isComputed = true;
                return Task.FromResult<IReadOnlySet<long>?>(new HashSet<long>());
            },
            TestContext.Current.CancellationToken);

        return isComputed;
    }

    private static StubXtreamHttpClientFactory Provider(string vodInfo) =>
        new(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_info"] = (HttpStatusCode.OK, vodInfo) });

    private static WatchHistoryRequest CreateRequest() =>
        new("https", "provider.example.com", 8443, new XtreamVideoStream(ContentType.Vod, "us er", "p&ss", "42"), StartedAtUtc);

    private static WatchHistoryRequest CreateEpisodeRequest(string episodeId) =>
        new("https", "provider.example.com", 8443, new XtreamVideoStream(ContentType.Series, "us er", "p&ss", episodeId), StartedAtUtc);

    private async Task<XtreamSource> CreateSourceAsync(string host = "provider.example.com")
    {
        var source = new XtreamSource { Protocol = "https", Host = host, Port = 8443 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return source;
    }

    private async Task AddMappingAsync(int xtreamSourceId, ContentType contentType, long? tmdbId)
    {
        _dbContext.StreamTmdbMappings.Add(new StreamTmdbMapping { XtreamSourceId = xtreamSourceId, ContentType = contentType, StreamId = "42", TmdbId = tmdbId });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task AddEpisodeAsync(int xtreamSourceId, string episodeId, string seriesId, int? seasonNumber, int? episodeNumber)
    {
        _dbContext.SeriesEpisodes.Add(new SeriesEpisode
        {
            XtreamSourceId = xtreamSourceId,
            EpisodeId = episodeId,
            SeriesId = seriesId,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _recommendationCache.Dispose();
        GC.SuppressFinalize(this);
    }
}
