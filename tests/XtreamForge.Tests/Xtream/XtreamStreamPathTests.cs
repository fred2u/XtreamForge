using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;

namespace XtreamForge.Tests.Xtream;

public class XtreamStreamPathTests
{
    [Theory]
    [InlineData("movie/user/secret/42.mkv")]
    [InlineData("MOVIE/user/secret/42.mp4")]
    [InlineData("movie/user/secret/42")]
    public void ParseVideo_ForAMovie_ReadsTheCredentialsAndTheStreamId(string path)
    {
        var movie = XtreamStreamPath.ParseVideo(path);

        Assert.Equal(new XtreamVideoStream(ContentType.Vod, "user", "secret", "42"), movie);
    }

    [Theory]
    [InlineData("series/user/secret/1001.mkv")]
    [InlineData("SERIES/user/secret/1001.mp4")]
    [InlineData("series/user/secret/1001")]
    public void ParseVideo_ForASeriesEpisode_ReadsTheCredentialsAndTheEpisodeId(string path)
    {
        var episode = XtreamStreamPath.ParseVideo(path);

        Assert.Equal(new XtreamVideoStream(ContentType.Series, "user", "secret", "1001"), episode);
    }

    [Theory]
    [InlineData("player_api.php")]
    [InlineData("live/user/secret/42.ts")]
    [InlineData("timeshift/user/secret/42.ts")]
    [InlineData("user/secret/42")]
    [InlineData("movie/user/secret")]
    [InlineData("movie/user/secret/extra/42.mkv")]
    [InlineData("series/user/secret/extra/42.mkv")]
    [InlineData("movie/user/secret/.mkv")]
    [InlineData("movie/user/secret/abc.mkv")]
    [InlineData("series/user/secret/abc.mkv")]
    [InlineData("movie//secret/42.mkv")]
    [InlineData("series/user//42.mkv")]
    public void ParseVideo_ForAnyOtherPath_ReturnsNull(string path)
    {
        Assert.Null(XtreamStreamPath.ParseVideo(path));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("1234567890123456789012345678901234567890123456789012345678901234", true)]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("12a", false)]
    [InlineData("-1", false)]
    public void IsStreamId_AcceptsOnlyNumbersOfAtMost64Digits(string? streamId, bool expected)
    {
        Assert.Equal(expected, XtreamStreamPath.IsStreamId(streamId));
    }

    [Fact]
    public void VideoStream_ToString_DoesNotExposeTheCredentials()
    {
        var text = new XtreamVideoStream(ContentType.Series, "user", "secret", "42").ToString();

        Assert.DoesNotContain("user", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.Contains("42", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("movie/user/secret/42.mkv")]
    [InlineData("series/user/secret/1001.mp4")]
    [InlineData("LIVE/user/secret/7.ts")]
    [InlineData("live/user/secret/7.m3u8")]
    [InlineData("timeshift/user/secret/60/2026-01-01:20-00/7.ts")]
    [InlineData("user/secret/7")]
    [InlineData("user/secret/7.ts")]
    public void IsStream_ForAMediaStreamPath_ReturnsTrue(string path)
    {
        Assert.True(XtreamStreamPath.IsStream(path));
    }

    [Theory]
    [InlineData("player_api.php")]
    [InlineData("xmltv.php")]
    [InlineData("movie/user/secret")]
    [InlineData("movie//secret/42.mkv")]
    [InlineData("images/posters/cover.jpg")]
    [InlineData("user/secret/abc")]
    [InlineData("user//7")]
    public void IsStream_ForAnyOtherPath_ReturnsFalse(string path)
    {
        Assert.False(XtreamStreamPath.IsStream(path));
    }
}
