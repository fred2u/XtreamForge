using System.Text.Json;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Tmdb;

public class TmdbIdMatcherTests
{
    private const string SingleMatrixResult = """{ "results": [{ "id": 603 }] }""";

    [Fact]
    public async Task FindTmdbIdAsync_WhenCandidateMatches_ReturnsTmdbIdAndSearchesWithYear()
    {
        var factory = new StubTmdbHttpClientFactory(new Dictionary<string, string>
        {
            ["search/movie"] = SingleMatrixResult,
            ["movie/603"] = TmdbTestData.MatrixDetails
        });

        var tmdbId = await FindAsync(factory, ContentType.Vod, TmdbTestData.MatrixProviderInfo);

        Assert.Equal(603, tmdbId);
        Assert.Equal(
            ["https://tmdb.example.com/3/search/movie?query=The%20Matrix&language=fr-FR&year=1999",
             "https://tmdb.example.com/3/movie/603?language=fr-FR&append_to_response=credits"],
            factory.RequestedUris.Select(uri => uri.AbsoluteUri));
    }

    [Fact]
    public async Task FindTmdbIdAsync_WhenSearchWithYearFindsNothing_RetriesWithoutYear()
    {
        var factory = new StubTmdbHttpClientFactory(uri => StubTmdbHttpClientFactory.GetRelativePath(uri) switch
        {
            "search/movie" when uri.Query.Contains("year=", StringComparison.Ordinal) => """{ "results": [] }""",
            "search/movie" => SingleMatrixResult,
            "movie/603" => TmdbTestData.MatrixDetails,
            _ => null
        });

        var tmdbId = await FindAsync(factory, ContentType.Vod, TmdbTestData.MatrixProviderInfo);

        Assert.Equal(603, tmdbId);
        Assert.Equal("?query=The%20Matrix&language=fr-FR", factory.RequestedUris[1].Query);
    }

    [Fact]
    public async Task FindTmdbIdAsync_WhenApiKeyIsMissing_DoesNotCallTmdb()
    {
        var factory = new StubTmdbHttpClientFactory(_ => SingleMatrixResult);

        var tmdbId = await FindAsync(factory, ContentType.Vod, TmdbTestData.MatrixProviderInfo, apiKey: "");

        Assert.Null(tmdbId);
        Assert.Empty(factory.RequestedUris);
    }

    [Fact]
    public async Task FindTmdbIdAsync_WhenSourceHasOnlyATitle_IsNotScorable()
    {
        var factory = new StubTmdbHttpClientFactory(_ => SingleMatrixResult);

        var tmdbId = await FindAsync(factory, ContentType.Vod, """{ "info": {}, "movie_data": { "name": "The Matrix" } }""");

        Assert.Null(tmdbId);
        Assert.Empty(factory.RequestedUris);
    }

    [Theory]
    [InlineData("""{ "results": [] }""")]
    [InlineData("""{ "results": [{ "id": 1 }, { "id": 2 }, { "id": 3 }, { "id": 4 }, { "id": 5 }, { "id": 6 }] }""")]
    public async Task FindTmdbIdAsync_WhenNoOrTooManyCandidates_ReturnsNullWithoutDetails(string searchResponse)
    {
        var factory = new StubTmdbHttpClientFactory(new Dictionary<string, string> { ["search/movie"] = searchResponse });

        var tmdbId = await FindAsync(factory, ContentType.Vod, TmdbTestData.MatrixProviderInfo);

        Assert.Null(tmdbId);
        Assert.DoesNotContain(factory.RequestedUris, uri => uri.AbsolutePath.Contains("/movie/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindTmdbIdAsync_WhenBestScoreIsBelowMinimumConfidence_ReturnsNull()
    {
        // same title only: 35 (title) + 5 (cast unknown) + 5 (genres unknown) + 10 (single candidate)
        var factory = new StubTmdbHttpClientFactory(new Dictionary<string, string>
        {
            ["search/movie"] = SingleMatrixResult,
            ["movie/603"] = """{ "id": 603, "title": "The Matrix", "original_title": "The Matrix", "release_date": "2010-01-01" }"""
        });

        var tmdbId = await FindAsync(factory, ContentType.Vod, """{ "info": { "releasedate": "1999-03-31" }, "movie_data": { "name": "The Matrix" } }""");

        Assert.Null(tmdbId);
    }

    [Fact]
    public async Task FindTmdbIdAsync_UsesConfiguredMinimumConfidenceScore()
    {
        var factory = new StubTmdbHttpClientFactory(new Dictionary<string, string>
        {
            ["search/movie"] = SingleMatrixResult,
            ["movie/603"] = TmdbTestData.MatrixDetails
        });

        var tmdbId = await FindAsync(factory, ContentType.Vod, TmdbTestData.MatrixProviderInfo, minimumConfidenceScore: 200);

        Assert.Null(tmdbId);
    }

    [Fact]
    public async Task FindTmdbIdAsync_KeepsTheBestScoredCandidate()
    {
        var factory = new StubTmdbHttpClientFactory(new Dictionary<string, string>
        {
            ["search/movie"] = """{ "results": [{ "id": 1 }, { "id": 603 }] }""",
            ["movie/1"] = """{ "id": 1, "title": "The Matrix", "original_title": "The Matrix", "release_date": "1999-01-01" }""",
            ["movie/603"] = TmdbTestData.MatrixDetails
        });

        var tmdbId = await FindAsync(factory, ContentType.Vod, TmdbTestData.MatrixProviderInfo);

        Assert.Equal(603, tmdbId);
    }

    [Fact]
    public async Task FindTmdbIdAsync_ForSeries_ScoresSeasonsWhenBasicScoreIsPromising()
    {
        var factory = new StubTmdbHttpClientFactory(new Dictionary<string, string>
        {
            ["search/tv"] = """{ "results": [{ "id": 1399 }] }""",
            // 35 (title) + 25 (date) + 5 + 5 + 10 (single candidate) = 80, below 85 without the seasons
            ["tv/1399"] = """{ "id": 1399, "name": "Game of Thrones", "original_name": "Game of Thrones", "first_air_date": "2011-04-17" }""",
            ["tv/1399/season/1"] = """{ "episodes": [{ "episode_number": 1, "air_date": "2011-04-17" }, { "episode_number": 2, "air_date": "2011-04-24" }] }"""
        });

        var tmdbId = await FindAsync(factory, ContentType.Series, SeriesProviderInfo("Game of Thrones", "2011-04-17"));

        Assert.Equal(1399, tmdbId);
        Assert.Contains(factory.RequestedUris, uri => uri.AbsolutePath.EndsWith("/tv/1399/season/1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindTmdbIdAsync_ForSeries_SkipsSeasonsWhenBasicScoreIsTooLow()
    {
        var factory = new StubTmdbHttpClientFactory(new Dictionary<string, string>
        {
            ["search/tv"] = """{ "results": [{ "id": 1399 }] }""",
            ["tv/1399"] = """{ "id": 1399, "name": "Something Else", "original_name": "Something Else", "first_air_date": "1990-01-01" }"""
        });

        var tmdbId = await FindAsync(factory, ContentType.Series, SeriesProviderInfo("Game of Thrones", "2011-04-17"));

        Assert.Null(tmdbId);
        Assert.DoesNotContain(factory.RequestedUris, uri => uri.AbsolutePath.Contains("/season/", StringComparison.Ordinal));
    }

    private static string SeriesProviderInfo(string name, string releaseDate) => $$"""
        {
          "info": { "name": "{{name}}", "releaseDate": "{{releaseDate}}" },
          "seasons": [{ "season_number": 1, "episode_count": 2 }],
          "episodes": {
            "1": [
              { "season": 1, "episode_num": 1, "info": { "air_date": "2011-04-17" } },
              { "season": 1, "episode_num": 2, "info": { "air_date": "2011-04-24" } }
            ]
          }
        }
        """;

    private static async Task<long?> FindAsync(StubTmdbHttpClientFactory factory, ContentType type, string providerInfo, string apiKey = "token", int minimumConfidenceScore = 85)
    {
        using var document = JsonDocument.Parse(providerInfo);
        var matcher = factory.CreateMatcher(apiKey, minimumConfidenceScore);

        return await matcher.FindTmdbIdAsync(type, document.RootElement, null, TestContext.Current.CancellationToken);
    }
}
