using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.TmdbInfos;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.TmdbInfos;

public class TmdbInfoServiceTests : IAsyncDisposable
{
    private const string LatticeInfo = """
        {
          "id": 603, "title": "Lattice", "original_title": "The Lattice", "release_date": "1999-03-30", "poster_path": "/lattice.jpg", "overview": "Orion", "vote_average": 8.2, "vote_count": 26000,
          "genres": [{ "id": 28, "name": "Action" }, { "id": 878, "name": "Science-Fiction" }]
        }
        """;

    private readonly XtreamForgeDbContext _dbContext;
    private readonly SteppingTimeProvider _time = new();

    public TmdbInfoServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Theory]
    [InlineData(ContentType.Vod, "https://tmdb.example.com/3/movie/603?language=fr-FR&append_to_response=credits")]
    [InlineData(ContentType.Series, "https://tmdb.example.com/3/tv/603?language=fr-FR&append_to_response=credits")]
    public async Task LoadAsync_StoresTmdbMetadata(ContentType type, string expectedUri)
    {
        var factory = new StubTmdbHttpClientFactory(_ => LatticeInfo);

        var loaded = await CreateService(factory).LoadAsync(new TmdbInfoRequest(type, 603), TestContext.Current.CancellationToken);

        Assert.True(loaded);
        Assert.Equal(expectedUri, Assert.Single(factory.RequestedUris).AbsoluteUri);
        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(type, info.ContentType);
        Assert.Equal("Lattice", info.Title);
        Assert.Equal("The Lattice", info.OriginalTitle);
        Assert.Equal(new DateOnly(1999, 3, 30), info.ReleaseDate);
        Assert.Equal("/lattice.jpg", info.PosterPath);
        Assert.Equal("Orion", info.Overview);
        Assert.Equal(8.2, info.VoteAverage);
        Assert.Equal(26000, info.VoteCount);
        Assert.Equal([28, 878], info.GenreIds);
        Assert.Equal(["Action", "Science Fiction"], info.Genres);
        Assert.Equal(_time.Now, info.LoadedAtUtc);
        Assert.Equal(_time.Now.AddDays(60), info.NextLoadAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task LoadAsync_WhenTmdbFailsTransiently_KeepsLoadedMetadataAndRetriesAfterAShortDelay(HttpStatusCode? statusCode)
    {
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Lattice", LoadedAtUtc = _time.Now.AddDays(-60), LoadAttemptCount = 1, NextLoadAtUtc = _time.Now });
        var factory = new StubTmdbHttpClientFactory(_ => throw new HttpRequestException("TMDB is unavailable.", null, statusCode));

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken));

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Lattice", info.Title);
        Assert.Equal(1, info.LoadAttemptCount);
        Assert.Equal(_time.Now + RetryDelay.TransientFailureDelay, info.NextLoadAtUtc);
    }

    [Fact]
    public async Task LoadAsync_ForTvShow_ReadsNameAndFirstAirDate()
    {
        var factory = new StubTmdbHttpClientFactory(_ => """{ "id": 1399, "name": "Crowns of Ash", "original_name": "Crowns of Ash", "first_air_date": "2011-04-17" }""");

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Series, 1399), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Crowns of Ash", info.Title);
        Assert.Equal(new DateOnly(2011, 4, 17), info.ReleaseDate);
    }

    [Fact]
    public async Task LoadAsync_ForMovie_StoresDirectorsTopTenCastAndRuntime()
    {
        var cast = string.Join(", ", Enumerable.Range(1, 12).Select(index => $$"""{ "name": "Actor {{index}}" }"""));
        var factory = new StubTmdbHttpClientFactory(_ => $$"""
            {
              "id": 603, "title": "Lattice", "runtime": 136,
              "credits": {
                "cast": [{ "name": " Aldo Ferrant " }, { "name": "" }, { "name": "Aldo Ferrant" }, {{cast}}],
                "crew": [{ "name": "Elena Marsh", "job": "Director" }, { "name": "Victor Lane", "job": "Producer" }, { "name": "Clara Marsh", "job": "Director" }]
              },
              "created_by": [{ "name": "Ignored for a movie" }]
            }
            """);

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Elena Marsh", "Clara Marsh"], info.Directors);
        Assert.Equal(["Aldo Ferrant", .. Enumerable.Range(1, 9).Select(index => $"Actor {index}")], info.Cast);
        Assert.Equal(136, info.DurationMinutes);
    }

    [Theory]
    [InlineData("""[55, 60]""", """{ "runtime": 50 }""", 55)]
    [InlineData("""[]""", """{ "runtime": 50 }""", 50)]
    [InlineData("""[0]""", "null", null)]
    public async Task LoadAsync_ForTvShow_StoresCreatorsAsDirectorsAndEpisodeRunTime(string episodeRunTime, string lastEpisodeToAir, int? expectedDuration)
    {
        var factory = new StubTmdbHttpClientFactory(_ => $$"""
            {
              "id": 1399, "name": "Crowns of Ash", "runtime": 999,
              "episode_run_time": {{episodeRunTime}}, "last_episode_to_air": {{lastEpisodeToAir}},
              "created_by": [{ "name": "Paul Dorsey" }, { "name": "R. J. Kemp" }],
              "credits": { "cast": [{ "name": "Nora Vale" }], "crew": [{ "name": "Episode Director", "job": "Director" }] }
            }
            """);

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Series, 1399), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Paul Dorsey", "R. J. Kemp"], info.Directors);
        Assert.Equal(["Nora Vale"], info.Cast);
        Assert.Equal(expectedDuration, info.DurationMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(5000)]
    public async Task LoadAsync_IgnoresOutOfRangeDuration(int runtime)
    {
        var factory = new StubTmdbHttpClientFactory(_ => $$"""{ "id": 603, "title": "Lattice", "runtime": {{runtime}} }""");

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(info.DurationMinutes);
    }

    [Fact]
    public async Task LoadAsync_SanitizesUntrustedValues()
    {
        var longTitle = new string('a', 600);
        var factory = new StubTmdbHttpClientFactory(_ => $$"""{ "id": 603, "title": "{{longTitle}}", "original_title": " ", "release_date": "not a date", "poster_path": "", "overview": "  Orion  ", "vote_average": 42, "vote_count": -1 }""");

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(500, info.Title?.Length);
        Assert.Null(info.OriginalTitle);
        Assert.Null(info.ReleaseDate);
        Assert.Null(info.PosterPath);
        Assert.Equal("Orion", info.Overview);
        Assert.Null(info.VoteAverage);
        Assert.Null(info.VoteCount);
    }

    [Fact]
    public async Task LoadAsync_WhenTmdbDoesNotKnowTheId_DefersNextLoadByOneDay()
    {
        var loaded = await CreateService(new StubTmdbHttpClientFactory(_ => null)).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        Assert.False(loaded);
        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(info.LoadedAtUtc);
        Assert.Equal(1, info.LoadAttemptCount);
        Assert.Equal(_time.Now.AddDays(1), info.NextLoadAtUtc);
    }

    [Fact]
    public async Task LoadAsync_WhenRefreshFails_KeepsLoadedMetadataAndDefersNextLoad()
    {
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Lattice", LoadedAtUtc = _time.Now.AddDays(-60), NextLoadAtUtc = _time.Now });

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(
            () => CreateService(new StubTmdbHttpClientFactory(_ => "not json")).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken));

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Lattice", info.Title);
        Assert.Equal(1, info.LoadAttemptCount);
        Assert.Equal(_time.Now.AddDays(1), info.NextLoadAtUtc);
    }

    [Fact]
    public async Task LoadAsync_WhenRefreshIsDue_ReplacesMetadata()
    {
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Old", LoadedAtUtc = _time.Now.AddDays(-60), NextLoadAtUtc = _time.Now });

        var loaded = await CreateService(new StubTmdbHttpClientFactory(_ => LatticeInfo)).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        Assert.True(loaded);
        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Lattice", info.Title);
        Assert.Equal(_time.Now.AddDays(60), info.NextLoadAtUtc);
    }

    [Fact]
    public async Task LoadAsync_WhenNextLoadIsNotDue_DoesNotCallTmdb()
    {
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, NextLoadAtUtc = _time.Now.AddMinutes(1) });
        var factory = new StubTmdbHttpClientFactory(_ => LatticeInfo);

        var loaded = await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        Assert.False(loaded);
        Assert.Empty(factory.RequestedUris);
    }

    [Fact]
    public async Task LoadAsync_WithoutApiKey_DoesNothing()
    {
        var factory = new StubTmdbHttpClientFactory(_ => LatticeInfo);

        var loaded = await CreateService(factory, apiKey: "").LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        Assert.False(loaded);
        Assert.Empty(factory.RequestedUris);
        Assert.False(await _dbContext.TmdbInfos.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_StoresValidGenreIdsWithTheirKnownEnglishNames()
    {
        var factory = new StubTmdbHttpClientFactory(_ => """
            { "id": 1399, "name": "Crowns of Ash", "genres": [{ "id": 10765 }, { "id": 99999 }, { "id": 10765 }, { "id": 0 }, { "id": -1 }, { "id": 18 }] }
            """);

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Series, 1399), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal([10765, 99999, 18], info.GenreIds);
        Assert.Equal(["Sci-Fi & Fantasy", "Drama"], info.Genres);
    }

    [Fact]
    public async Task LoadAsync_WithoutGenresOrCredits_StoresEmptyLists()
    {
        await CreateService(new StubTmdbHttpClientFactory(_ => """{ "id": 603, "title": "Lattice" }""")).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Empty(info.GenreIds);
        Assert.Empty(info.Genres);
        Assert.Empty(info.Directors);
        Assert.Empty(info.Cast);
    }

    [Fact]
    public async Task GetAsync_ReturnsOnlyRequestedIdsOfRequestedContentType()
    {
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Lattice" });
        await AddInfoAsync(new TmdbInfo { TmdbId = 604, ContentType = ContentType.Vod, Title = "Lattice Returns" });
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Series, Title = "Show" });

        var infos = await CreateService(new StubTmdbHttpClientFactory(_ => null)).GetAsync(ContentType.Vod, [603, 999], TestContext.Current.CancellationToken);

        Assert.Equal("Lattice", Assert.Single(infos.Infos).Value.Title);
        Assert.Empty(infos.ExcludedTmdbIds);
        Assert.Empty(_dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetAsync_ReturnsOnlyTheIdsOfManuallyExcludedEntries()
    {
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Lattice", IsExcluded = true });
        await AddInfoAsync(new TmdbInfo { TmdbId = 604, ContentType = ContentType.Vod, Title = "Lattice Returns" });

        var infos = await CreateService(new StubTmdbHttpClientFactory(_ => null)).GetAsync(ContentType.Vod, [603, 604], TestContext.Current.CancellationToken);

        Assert.Equal([604L], infos.Infos.Keys);
        Assert.Equal([603L], infos.ExcludedTmdbIds);
    }

    private TmdbInfoService CreateService(StubTmdbHttpClientFactory factory, string apiKey = "token")
        => factory.CreateTmdbInfoService(_dbContext, _time, apiKey);

    private async Task AddInfoAsync(TmdbInfo info)
    {
        _dbContext.TmdbInfos.Add(info);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
