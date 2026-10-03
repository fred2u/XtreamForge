using System.Net;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services;

public class WatchHistoryServiceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 10, 1, 20, 30, 0, TimeSpan.Zero);

    private readonly XtreamForgeDbContext _dbContext;
    private readonly TmdbIdCache _recommendationCache = new(TimeProvider.System);

    public WatchHistoryServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Fact]
    public async Task RecordAsync_InvalidatesTheCachedRecommendations()
    {
        var source = await CreateSourceAsync();
        await AddMappingAsync(source.Id, ContentType.Vod, 603);
        var computeCount = 0;
        Task<IReadOnlySet<long>?> ComputeAsync(CancellationToken _)
        {
            computeCount++;
            return Task.FromResult<IReadOnlySet<long>?>(new HashSet<long> { 604 });
        }

        await _recommendationCache.GetOrComputeAsync(TmdbIdCache.RecommendationsKey, ComputeAsync, TestContext.Current.CancellationToken);
        await new WatchHistoryService(Provider("{}"), _dbContext, _recommendationCache).RecordAsync(CreateRequest(), TestContext.Current.CancellationToken);
        await _recommendationCache.GetOrComputeAsync(TmdbIdCache.RecommendationsKey, ComputeAsync, TestContext.Current.CancellationToken);

        Assert.Equal(2, computeCount);
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
        Assert.Equal((ContentType.Vod, 603L, StartedAtUtc), (entry.ContentType, entry.TmdbId, entry.StartedAtUtc));
        Assert.Empty(httpClientFactory.RequestedUris);
    }

    [Fact]
    public async Task RecordAsync_WhenTheMovieIsNotMapped_RecordsTheProviderTmdbId()
    {
        await CreateSourceAsync();
        var httpClientFactory = Provider("""{ "info": [], "movie_data": { "tmdb_id": 603 } }""");

        var recorded = await new WatchHistoryService(httpClientFactory, _dbContext, _recommendationCache).RecordAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.True(recorded);
        Assert.Equal(603, (await _dbContext.WatchHistory.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).TmdbId);
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

    private static StubXtreamHttpClientFactory Provider(string vodInfo) =>
        new(new Dictionary<string, (HttpStatusCode, string)> { ["get_vod_info"] = (HttpStatusCode.OK, vodInfo) });

    private static WatchHistoryRequest CreateRequest() =>
        new("https", "provider.example.com", 8443, new XtreamMovieStream("us er", "p&ss", "42"), StartedAtUtc);

    private async Task<XtreamSource> CreateSourceAsync()
    {
        var source = new XtreamSource { Protocol = "https", Host = "provider.example.com", Port = 8443 };
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

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _recommendationCache.Dispose();
        GC.SuppressFinalize(this);
    }
}
