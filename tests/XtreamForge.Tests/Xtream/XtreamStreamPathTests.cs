using XtreamForge.ApiService.Xtream;

namespace XtreamForge.Tests.Xtream;

public class XtreamStreamPathTests
{
    [Theory]
    [InlineData("movie/user/secret/42.mkv")]
    [InlineData("MOVIE/user/secret/42.mp4")]
    [InlineData("movie/user/secret/42")]
    public void ParseMovie_ReadsTheCredentialsAndTheStreamId(string path)
    {
        var movie = XtreamStreamPath.ParseMovie(path);

        Assert.Equal(new XtreamMovieStream("user", "secret", "42"), movie);
    }

    [Theory]
    [InlineData("player_api.php")]
    [InlineData("series/user/secret/42.mkv")]
    [InlineData("live/user/secret/42.ts")]
    [InlineData("user/secret/42")]
    [InlineData("movie/user/secret")]
    [InlineData("movie/user/secret/extra/42.mkv")]
    [InlineData("movie/user/secret/.mkv")]
    [InlineData("movie/user/secret/abc.mkv")]
    [InlineData("movie//secret/42.mkv")]
    [InlineData("movie/user//42.mkv")]
    public void ParseMovie_ForAnyOtherPath_ReturnsNull(string path)
    {
        Assert.Null(XtreamStreamPath.ParseMovie(path));
    }

    [Fact]
    public void MovieStream_ToString_DoesNotExposeTheCredentials()
    {
        var text = new XtreamMovieStream("user", "secret", "42").ToString();

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
