using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.TmdbInfos;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.TmdbInfos;

public class TmdbInfoServiceTests : IAsyncDisposable
{
    private const string MatrixInfo = """
        {
          "id": 603, "title": "Matrix", "original_title": "The Matrix", "release_date": "1999-03-30", "poster_path": "/matrix.jpg", "overview": "Neo", "vote_average": 8.2, "vote_count": 26000,
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
        var factory = new StubTmdbHttpClientFactory(_ => MatrixInfo);

        var loaded = await CreateService(factory).LoadAsync(new TmdbInfoRequest(type, 603), TestContext.Current.CancellationToken);

        Assert.True(loaded);
        Assert.Equal(expectedUri, Assert.Single(factory.RequestedUris).AbsoluteUri);
        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(type, info.ContentType);
        Assert.Equal("Matrix", info.Title);
        Assert.Equal("The Matrix", info.OriginalTitle);
        Assert.Equal(new DateOnly(1999, 3, 30), info.ReleaseDate);
        Assert.Equal("/matrix.jpg", info.PosterPath);
        Assert.Equal("Neo", info.Overview);
        Assert.Equal(8.2, info.VoteAverage);
        Assert.Equal(26000, info.VoteCount);
        Assert.Equal([28, 878], info.GenreIds);
        Assert.Equal(["Action", "Science Fiction"], info.Genres);
        Assert.Equal(_time.Now, info.LoadedAtUtc);
        Assert.Equal(_time.Now.AddDays(60), info.NextLoadAtUtc);
    }

    [Fact]
    public async Task LoadAsync_ForTvShow_ReadsNameAndFirstAirDate()
    {
        var factory = new StubTmdbHttpClientFactory(_ => """{ "id": 1399, "name": "Game of Thrones", "original_name": "Game of Thrones", "first_air_date": "2011-04-17" }""");

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Series, 1399), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Game of Thrones", info.Title);
        Assert.Equal(new DateOnly(2011, 4, 17), info.ReleaseDate);
    }

    [Fact]
    public async Task LoadAsync_ForMovie_StoresDirectorsTopTenCastAndRuntime()
    {
        var cast = string.Join(", ", Enumerable.Range(1, 12).Select(index => $$"""{ "name": "Actor {{index}}" }"""));
        var factory = new StubTmdbHttpClientFactory(_ => $$"""
            {
              "id": 603, "title": "Matrix", "runtime": 136,
              "credits": {
                "cast": [{ "name": " Keanu Reeves " }, { "name": "" }, { "name": "Keanu Reeves" }, {{cast}}],
                "crew": [{ "name": "Lana Wachowski", "job": "Director" }, { "name": "Joel Silver", "job": "Producer" }, { "name": "Lilly Wachowski", "job": "Director" }]
              },
              "created_by": [{ "name": "Ignored for a movie" }]
            }
            """);

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Lana Wachowski", "Lilly Wachowski"], info.Directors);
        Assert.Equal(["Keanu Reeves", .. Enumerable.Range(1, 9).Select(index => $"Actor {index}")], info.Cast);
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
              "id": 1399, "name": "Game of Thrones", "runtime": 999,
              "episode_run_time": {{episodeRunTime}}, "last_episode_to_air": {{lastEpisodeToAir}},
              "created_by": [{ "name": "David Benioff" }, { "name": "D. B. Weiss" }],
              "credits": { "cast": [{ "name": "Emilia Clarke" }], "crew": [{ "name": "Episode Director", "job": "Director" }] }
            }
            """);

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Series, 1399), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["David Benioff", "D. B. Weiss"], info.Directors);
        Assert.Equal(["Emilia Clarke"], info.Cast);
        Assert.Equal(expectedDuration, info.DurationMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(5000)]
    public async Task LoadAsync_IgnoresOutOfRangeDuration(int runtime)
    {
        var factory = new StubTmdbHttpClientFactory(_ => $$"""{ "id": 603, "title": "Matrix", "runtime": {{runtime}} }""");

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(info.DurationMinutes);
    }

    [Fact]
    public async Task LoadAsync_SanitizesUntrustedValues()
    {
        var longTitle = new string('a', 600);
        var factory = new StubTmdbHttpClientFactory(_ => $$"""{ "id": 603, "title": "{{longTitle}}", "original_title": " ", "release_date": "not a date", "poster_path": "", "overview": "  Neo  ", "vote_average": 42, "vote_count": -1 }""");

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(500, info.Title?.Length);
        Assert.Null(info.OriginalTitle);
        Assert.Null(info.ReleaseDate);
        Assert.Null(info.PosterPath);
        Assert.Equal("Neo", info.Overview);
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
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Matrix", LoadedAtUtc = _time.Now.AddDays(-60), NextLoadAtUtc = _time.Now });

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(
            () => CreateService(new StubTmdbHttpClientFactory(_ => "not json")).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken));

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Matrix", info.Title);
        Assert.Equal(1, info.LoadAttemptCount);
        Assert.Equal(_time.Now.AddDays(1), info.NextLoadAtUtc);
    }

    [Fact]
    public async Task LoadAsync_WhenRefreshIsDue_ReplacesMetadata()
    {
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Old", LoadedAtUtc = _time.Now.AddDays(-60), NextLoadAtUtc = _time.Now });

        var loaded = await CreateService(new StubTmdbHttpClientFactory(_ => MatrixInfo)).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        Assert.True(loaded);
        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Matrix", info.Title);
        Assert.Equal(_time.Now.AddDays(60), info.NextLoadAtUtc);
    }

    [Fact]
    public async Task LoadAsync_WhenNextLoadIsNotDue_DoesNotCallTmdb()
    {
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, NextLoadAtUtc = _time.Now.AddMinutes(1) });
        var factory = new StubTmdbHttpClientFactory(_ => MatrixInfo);

        var loaded = await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        Assert.False(loaded);
        Assert.Empty(factory.RequestedUris);
    }

    [Fact]
    public async Task LoadAsync_WithoutApiKey_DoesNothing()
    {
        var factory = new StubTmdbHttpClientFactory(_ => MatrixInfo);

        var loaded = await CreateService(factory, apiKey: "").LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        Assert.False(loaded);
        Assert.Empty(factory.RequestedUris);
        Assert.False(await _dbContext.TmdbInfos.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_StoresValidGenreIdsWithTheirKnownEnglishNames()
    {
        var factory = new StubTmdbHttpClientFactory(_ => """
            { "id": 1399, "name": "Game of Thrones", "genres": [{ "id": 10765 }, { "id": 99999 }, { "id": 10765 }, { "id": 0 }, { "id": -1 }, { "id": 18 }] }
            """);

        await CreateService(factory).LoadAsync(new TmdbInfoRequest(ContentType.Series, 1399), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal([10765, 99999, 18], info.GenreIds);
        Assert.Equal(["Sci-Fi & Fantasy", "Drama"], info.Genres);
    }

    [Fact]
    public async Task LoadAsync_WithoutGenresOrCredits_StoresEmptyLists()
    {
        await CreateService(new StubTmdbHttpClientFactory(_ => """{ "id": 603, "title": "Matrix" }""")).LoadAsync(new TmdbInfoRequest(ContentType.Vod, 603), TestContext.Current.CancellationToken);

        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Empty(info.GenreIds);
        Assert.Empty(info.Genres);
        Assert.Empty(info.Directors);
        Assert.Empty(info.Cast);
    }

    [Fact]
    public async Task GetAsync_ReturnsOnlyRequestedIdsOfRequestedContentType()
    {
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Matrix" });
        await AddInfoAsync(new TmdbInfo { TmdbId = 604, ContentType = ContentType.Vod, Title = "Matrix Reloaded" });
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Series, Title = "Show" });

        var infos = await CreateService(new StubTmdbHttpClientFactory(_ => null)).GetAsync(ContentType.Vod, [603, 999], TestContext.Current.CancellationToken);

        Assert.Equal("Matrix", Assert.Single(infos.Infos).Value.Title);
        Assert.Empty(infos.ExcludedTmdbIds);
        Assert.Empty(_dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetAsync_ReturnsOnlyTheIdsOfManuallyExcludedEntries()
    {
        await AddInfoAsync(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Matrix", IsExcluded = true });
        await AddInfoAsync(new TmdbInfo { TmdbId = 604, ContentType = ContentType.Vod, Title = "Matrix Reloaded" });

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
