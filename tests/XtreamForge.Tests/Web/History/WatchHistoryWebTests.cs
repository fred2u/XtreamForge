using XtreamForge.Web.Features.Categories;
using XtreamForge.Web.Features.Dashboard;
using XtreamForge.Web.Features.History;

namespace XtreamForge.Tests.Web.History;

public class WatchHistoryWebTests
{
    [Theory]
    [InlineData(1, 2, "S01E02")]
    [InlineData(12, 105, "S12E105")]
    [InlineData(0, 1, "S00E01")]
    [InlineData(2, null, "S02")]
    [InlineData(null, 7, "E07")]
    [InlineData(null, null, null)]
    public void EpisodeLabel_ShowsTheKnownSeasonAndEpisode(int? seasonNumber, int? episodeNumber, string? expected)
    {
        Assert.Equal(expected, CreateEntry(ContentType.Series, seasonNumber, episodeNumber).EpisodeLabel);
    }

    [Fact]
    public void Subtitle_ForAnEpisode_StartsWithTheEpisode()
    {
        var entry = CreateEntry(ContentType.Series, 1, 2);

        Assert.Equal("S01E02 · TMDB #1399 · 2011 · Crowns of Iron", entry.Subtitle);
        Assert.Equal("Crowns S01E02", entry.FullTitle);
        Assert.Equal("https://www.themoviedb.org/tv/1399", entry.TmdbUrl);
    }

    [Fact]
    public void Subtitle_ForAMovie_HasNoEpisode()
    {
        var entry = CreateEntry(ContentType.Vod, null, null);

        Assert.Equal("TMDB #1399 · 2011 · Crowns of Iron", entry.Subtitle);
        Assert.Equal("Crowns", entry.FullTitle);
    }

    [Fact]
    public void WatchActivityDay_CountsTheMovieAndEpisodePlaybacks()
    {
        Assert.Equal(5, new WatchActivityDayDto(new DateOnly(2026, 10, 1), 2, 3).Count);
    }

    private static WatchHistoryEntryDto CreateEntry(ContentType contentType, int? seasonNumber, int? episodeNumber) => new(
        1,
        contentType,
        1399,
        seasonNumber,
        episodeNumber,
        new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero),
        "Crowns",
        "Crowns of Iron",
        new DateOnly(2011, 4, 17),
        null);
}
