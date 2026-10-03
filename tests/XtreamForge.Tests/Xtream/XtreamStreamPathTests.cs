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
}
