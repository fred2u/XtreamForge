using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.History;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.VirtualCategories;

public class RecommendationServiceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly XtreamForgeDbContext _dbContext;
    private readonly Dictionary<string, string> _responses = [];
    private readonly StubTmdbHttpClientFactory _tmdb;
    private readonly TmdbIdCache _cache = new(TimeProvider.System);

    public RecommendationServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _tmdb = new StubTmdbHttpClientFactory(_responses);
    }

    [Fact]
    public async Task Get_NeverRecommendsAWatchedMovie()
    {
        await WatchAsync(603, 604);
        _responses["movie/603/recommendations"] = Recommendations(604, 605);
        _responses["movie/604/recommendations"] = Recommendations(603, 606);

        var recommendations = await CreateService().GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal([605L, 606L], recommendations.Select(recommendation => recommendation.TmdbId).Order());
    }

    [Fact]
    public async Task Get_RanksTheMoviesRecommendedForSeveralAndRecentMoviesFirst()
    {
        // 604 is the most recently watched movie
        await WatchAsync(603, 604);
        _responses["movie/603/recommendations"] = Recommendations(700, 701);
        _responses["movie/604/recommendations"] = Recommendations(702, 701);

        var recommendations = await CreateService().GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal([701L, 702L, 700L], recommendations.Select(recommendation => recommendation.TmdbId));
        Assert.Equal(2, recommendations[0].RecommendedByCount);
    }

    [Fact]
    public async Task Get_ExcludesManuallyExcludedMoviesAndFlagsTheMoviesOfTheCatalogue()
    {
        await WatchAsync(603);
        _dbContext.TmdbInfos.AddRange(
            new TmdbInfo { ContentType = ContentType.Vod, TmdbId = 700, IsExcluded = true },
            new TmdbInfo { ContentType = ContentType.Vod, TmdbId = 701 },
            new TmdbInfo { ContentType = ContentType.Series, TmdbId = 702 });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _responses["movie/603/recommendations"] = Recommendations(700, 701, 702);

        var recommendations = await CreateService().GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [(701L, true), (702L, false)],
            recommendations.Select(recommendation => (recommendation.TmdbId, recommendation.IsInCatalogue)));
    }

    [Fact]
    public async Task Get_IgnoresTheSeriesOfTheHistory()
    {
        _dbContext.WatchHistory.Add(new WatchHistoryEntry { ContentType = ContentType.Series, TmdbId = 1399, StartedAtUtc = Day });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var recommendations = await CreateService().GetAsync(TestContext.Current.CancellationToken);

        Assert.Empty(recommendations);
        Assert.Empty(_tmdb.RequestedUris);
    }

    [Fact]
    public async Task Get_WithoutApiKey_DoesNotCallTmdb()
    {
        await WatchAsync(603);

        var recommendations = await CreateService(apiKey: "").GetAsync(TestContext.Current.CancellationToken);

        Assert.Empty(recommendations);
        Assert.Empty(_tmdb.RequestedUris);
    }

    [Fact]
    public async Task Get_MapsTheTmdbValues()
    {
        await WatchAsync(603);
        _responses["movie/603/recommendations"] = """
            { "results": [ { "id": 604, "title": "Lattice Returns", "original_title": "The Lattice Returns", "release_date": "2003-05-15", "poster_path": "/reloaded.jpg", "vote_average": 7.1, "vote_count": 1200, "genre_ids": [28, 999999] } ] }
            """;

        var recommendations = await CreateService().GetAsync(TestContext.Current.CancellationToken);

        var recommendation = Assert.Single(recommendations);
        Assert.Equal(
            ("Lattice Returns", "The Lattice Returns", new DateOnly(2003, 5, 15), "/reloaded.jpg", 7.1, (int?)1200),
            (recommendation.Title, recommendation.OriginalTitle, recommendation.ReleaseDate, recommendation.PosterPath, recommendation.VoteAverage, recommendation.VoteCount));
        // unknown genre IDs have no name
        Assert.Equal(["Action"], recommendation.Genres);
    }

    [Fact]
    public async Task GetRecommendedTmdbIds_IsCachedUntilTheCacheIsInvalidated()
    {
        await WatchAsync(603);
        _responses["movie/603/recommendations"] = Recommendations(604);
        var service = CreateService();

        Assert.Equal([604L], await service.GetRecommendedTmdbIdsAsync(TestContext.Current.CancellationToken));
        _responses["movie/603/recommendations"] = Recommendations(605);
        Assert.Equal([604L], await service.GetRecommendedTmdbIdsAsync(TestContext.Current.CancellationToken));
        Assert.Single(_tmdb.RequestedUris);

        _cache.Invalidate(TmdbIdCache.RecommendationsKey);

        Assert.Equal([605L], await service.GetRecommendedTmdbIdsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetRecommendedTmdbIds_WhenTmdbFails_ReturnsNoRecommendationAndRetriesAfterTheFailureDuration()
    {
        await WatchAsync(603);
        var failing = true;
        var tmdb = new StubTmdbHttpClientFactory(_ => failing ? throw new HttpRequestException("TMDB is down") : Recommendations(604));
        var time = new SteppingTimeProvider();
        using var cache = new TmdbIdCache(time);
        var service = tmdb.CreateRecommendationService(_dbContext, cache);

        Assert.Empty(await service.GetRecommendedTmdbIdsAsync(TestContext.Current.CancellationToken));

        failing = false;

        Assert.Empty(await service.GetRecommendedTmdbIdsAsync(TestContext.Current.CancellationToken));
        Assert.Single(tmdb.RequestedUris);

        time.Now += TmdbIdCache.FailureDuration;
        Assert.Equal([604L], await service.GetRecommendedTmdbIdsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetRecommendedTmdbIds_WhenTheWatchHistoryChangesDuringTheFailureDuration_RetriesAtOnce()
    {
        await WatchAsync(603);
        var failing = true;
        var tmdb = new StubTmdbHttpClientFactory(_ => failing ? throw new HttpRequestException("TMDB is down") : Recommendations(604));
        var service = tmdb.CreateRecommendationService(_dbContext, _cache);

        Assert.Empty(await service.GetRecommendedTmdbIdsAsync(TestContext.Current.CancellationToken));
        failing = false;
        _cache.Invalidate(TmdbIdCache.RecommendationsKey);

        Assert.Equal([604L], await service.GetRecommendedTmdbIdsAsync(TestContext.Current.CancellationToken));
    }

    private RecommendationService CreateService(string apiKey = "token") => _tmdb.CreateRecommendationService(_dbContext, _cache, apiKey);

    // records one playback per movie, in the given order
    private async Task WatchAsync(params long[] tmdbIds)
    {
        foreach (var (tmdbId, index) in tmdbIds.Select((tmdbId, index) => (tmdbId, index)))
        {
            _dbContext.WatchHistory.Add(new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = tmdbId, StartedAtUtc = Day.AddHours(index) });
            await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private static string Recommendations(params long[] tmdbIds)
        => $$"""{ "results": [{{string.Join(", ", tmdbIds.Select(tmdbId => $$"""{ "id": {{tmdbId}} }"""))}}] }""";

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }
}
