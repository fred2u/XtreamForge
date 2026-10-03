using System.Text.Json;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Domain.Enums;

namespace XtreamForge.Tests.Services.Tmdb;

public class TmdbSourceItemParserTests
{
    [Fact]
    public void Parse_ForVod_ReadsInfoAndMovieData()
    {
        var source = Parse(ContentType.Vod, TmdbTestData.MatrixProviderInfo, null);

        Assert.Equal("|FR| The Matrix (1999) 4K", source.RawTitle);
        Assert.Equal("The Matrix", source.Title);
        Assert.Equal("f89U3ADr1oiB1s9GkdPOEpXUk5H", source.PosterId);
        Assert.Equal(new DateTime(1999, 3, 31, 0, 0, 0, DateTimeKind.Unspecified), source.ReleaseDate);
        Assert.Equal(["keanu reeves", "laurence fishburne"], source.Cast.Order());
        Assert.Equal(["action", "science fiction"], source.Genres.Order());
        Assert.True(source.IsScorable);
    }

    [Fact]
    public void Parse_ForVod_PrefersStreamIconAsPoster()
    {
        var source = Parse(ContentType.Vod, TmdbTestData.MatrixProviderInfo, "http://images.example.com/posters/streamicon123.png");

        Assert.Equal("streamicon123", source.PosterId);
    }

    [Fact]
    public void Parse_ForVod_WithoutMovieData_IsNotScorable()
    {
        var source = Parse(ContentType.Vod, """{ "info": { "name": "The Matrix", "releasedate": "1999-03-31" } }""", null);

        Assert.False(source.IsScorable);
    }

    [Fact]
    public void Parse_ForSeries_ReadsSeasonsAndEpisodes()
    {
        var source = Parse(ContentType.Series, """
            {
              "info": { "name": "Game of Thrones", "cover": "http://img/abc1234567.jpg", "releaseDate": "2011-04-17", "genre": "Drama / Fantasy" },
              "seasons": [{ "season_number": "1", "episode_count": 10 }],
              "episodes": { "1": [{ "season": 1, "episode_num": "2", "info": { "air_date": "2011-04-24" } }] }
            }
            """, null);

        Assert.Equal("Game of Thrones", source.Title);
        Assert.Equal("abc1234567", source.PosterId);
        Assert.Equal(["drama", "fantasy"], source.Genres.Order());
        Assert.Equal([new TmdbSourceSeason(1, 10)], source.Seasons);
        Assert.Equal([new TmdbSourceEpisode(1, 2, new DateTime(2011, 4, 24, 0, 0, 0, DateTimeKind.Unspecified))], source.Episodes);
    }

    [Fact]
    public void Parse_WhenPayloadIsNotAnObject_ReturnsUnscorableItem()
    {
        Assert.False(Parse(ContentType.Series, "[]", null).IsScorable);
    }

    private static TmdbSourceItem Parse(ContentType type, string json, string? streamIcon)
    {
        using var document = JsonDocument.Parse(json);
        return TmdbSourceItemParser.Parse(type, document.RootElement, streamIcon);
    }
}
