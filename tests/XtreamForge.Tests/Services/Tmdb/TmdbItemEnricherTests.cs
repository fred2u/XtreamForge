using System.Text.Json.Nodes;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.Tests.Services.Tmdb;

public class TmdbItemEnricherTests
{
    private const string ImageBaseUrl = "https://image.tmdb.org/t/p/";

    private const string ProviderItem = """
        {
          "name": "Provider name", "o_name": "Provider name", "title": "Provider title", "year": "1990", "release_date": "1990",
          "releasedate": "1990-01-01", "releaseDate": "1990-01-01", "stream_icon": "icon", "movie_image": "image", "cover_big": "cover_big", "cover": "cover",
          "rating": 5, "rating_5based": "2.5", "plot": "Provider plot", "description": "Provider description",
          "director": "Provider director", "cast": "Provider cast", "actors": "Provider actors", "genre": "Science-fiction",
          "duration_secs": 7200, "duration": "02:00:00", "episode_run_time": "120"
        }
        """;

    [Fact]
    public void Apply_ReplacesEveryKnownField()
    {
        var item = Parse(ProviderItem);

        TmdbItemEnricher.Apply(item, CreateInfo(), ImageBaseUrl);

        Assert.Equal("Matrix | 1999", item["name"]?.GetValue<string>());
        Assert.Equal("Matrix | 1999", item["o_name"]?.GetValue<string>());
        Assert.Equal("Matrix | 1999", item["title"]?.GetValue<string>());
        Assert.Equal("1999", item["year"]?.GetValue<string>());
        Assert.Equal("1999", item["release_date"]?.GetValue<string>());
        Assert.Equal("1999-03-30", item["releasedate"]?.GetValue<string>());
        Assert.Equal("1999-03-30", item["releaseDate"]?.GetValue<string>());
        Assert.Equal("https://image.tmdb.org/t/p/w342/matrix.jpg", item["stream_icon"]?.GetValue<string>());
        Assert.Equal("https://image.tmdb.org/t/p/w780/matrix.jpg", item["movie_image"]?.GetValue<string>());
        Assert.Equal("https://image.tmdb.org/t/p/w780/matrix.jpg", item["cover_big"]?.GetValue<string>());
        Assert.Equal("https://image.tmdb.org/t/p/w780/matrix.jpg", item["cover"]?.GetValue<string>());
        Assert.Equal(8.2, item["rating"]?.GetValue<double>());
        Assert.Equal("4.1", item["rating_5based"]?.GetValue<string>());
        Assert.Equal("Neo", item["plot"]?.GetValue<string>());
        Assert.Equal("Neo", item["description"]?.GetValue<string>());
        Assert.Equal("Lana Wachowski, Lilly Wachowski", item["director"]?.GetValue<string>());
        Assert.Equal("Keanu Reeves, Carrie-Anne Moss", item["cast"]?.GetValue<string>());
        Assert.Equal("Keanu Reeves, Carrie-Anne Moss", item["actors"]?.GetValue<string>());
        Assert.Equal(8160, item["duration_secs"]?.GetValue<double>());
        Assert.Equal("02:16:00", item["duration"]?.GetValue<string>());
        Assert.Equal("136", item["episode_run_time"]?.GetValue<string>());
        // the provider genre is kept: the stored TMDB genres are English names
        Assert.Equal("Science-fiction", item["genre"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("""{ "genre": "" }""")]
    [InlineData("""{ "genre": " " }""")]
    [InlineData("""{ "genre": null }""")]
    [InlineData("""{ "genre": [] }""")]
    public void Apply_WhenProviderGenreIsEmpty_UsesTmdbGenres(string json)
    {
        var item = Parse(json);

        TmdbItemEnricher.Apply(item, CreateInfo(), ImageBaseUrl);

        Assert.Equal("Action, Science Fiction", item["genre"]?.GetValue<string>());
    }

    [Fact]
    public void Apply_WhenProviderGenreIsEmptyAndTmdbHasNoGenre_KeepsProviderValue()
    {
        var item = Parse("""{ "genre": "" }""");
        var info = CreateInfo();
        info.Genres = [];

        TmdbItemEnricher.Apply(item, info, ImageBaseUrl);

        Assert.Equal("", item["genre"]?.GetValue<string>());
    }

    [Fact]
    public void Apply_ForTvShow_ReplacesEpisodeRunTimeKeepingNumericKind()
    {
        var item = Parse("""{ "episode_run_time": 45 }""");
        var info = CreateInfo();
        info.DurationMinutes = 52;

        TmdbItemEnricher.Apply(item, info, ImageBaseUrl);

        Assert.Equal(52, item["episode_run_time"]?.GetValue<double>());
    }

    [Fact]
    public void Apply_WhenTmdbValuesAreMissing_KeepsProviderValues()
    {
        var item = Parse(ProviderItem);
        var expected = item.ToJsonString();
        var info = new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, VoteAverage = 8.2, VoteCount = 0 };

        TmdbItemEnricher.Apply(item, info, ImageBaseUrl);

        Assert.Equal(expected, item.ToJsonString());
    }

    [Fact]
    public void Apply_WithoutReleaseDate_FormatsTitleWithoutYear()
    {
        var item = Parse("""{ "name": "Provider name", "year": "1990" }""");
        var info = CreateInfo();
        info.ReleaseDate = null;

        TmdbItemEnricher.Apply(item, info, ImageBaseUrl);

        Assert.Equal("Matrix", item["name"]?.GetValue<string>());
        Assert.Equal("1990", item["year"]?.GetValue<string>());
    }

    [Fact]
    public void Apply_DoesNotAddMissingKeys()
    {
        var item = Parse("""{ "stream_id": 1 }""");

        TmdbItemEnricher.Apply(item, CreateInfo(), ImageBaseUrl);

        Assert.Equal("""{"stream_id":1}""", item.ToJsonString());
    }

    [Theory]
    [InlineData("https://image.tmdb.org/t/p", "/a.jpg", "https://image.tmdb.org/t/p/w342/a.jpg")]
    [InlineData("https://image.tmdb.org/t/p/", "a.jpg", "https://image.tmdb.org/t/p/w342/a.jpg")]
    [InlineData("https://image.tmdb.org/t/p/", " ", null)]
    [InlineData("https://image.tmdb.org/t/p/", null, null)]
    [InlineData("not a url", "/a.jpg", null)]
    [InlineData("ftp://image.tmdb.org/t/p/", "/a.jpg", null)]
    public void BuildImageUrl_ReturnsOnlyValidHttpUrls(string imageBaseUrl, string? imagePath, string? expectedUrl)
    {
        Assert.Equal(expectedUrl, TmdbItemEnricher.BuildImageUrl(imageBaseUrl, TmdbItemEnricher.MediumPosterSize, imagePath));
    }

    private static TmdbInfo CreateInfo() => new()
    {
        TmdbId = 603,
        ContentType = ContentType.Vod,
        Title = "Matrix",
        ReleaseDate = new DateOnly(1999, 3, 30),
        PosterPath = "/matrix.jpg",
        Overview = "Neo",
        VoteAverage = 8.2,
        VoteCount = 26000,
        GenreIds = [28, 878],
        Genres = ["Action", "Science Fiction"],
        Directors = ["Lana Wachowski", "Lilly Wachowski"],
        Cast = ["Keanu Reeves", "Carrie-Anne Moss"],
        DurationMinutes = 136
    };

    private static JsonObject Parse(string json) => Assert.IsType<JsonObject>(JsonNode.Parse(json));
}
