using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.Tests.ServiceDefaults;

public class XtreamCredentialRedactionTests
{
    private const string Password = "secret";

    [Theory]
    [InlineData("movie/user/secret/42.mp4", "movie/***/***/42.mp4")]
    [InlineData("/movie/user/secret/42.mp4", "/movie/***/***/42.mp4")]
    [InlineData("/http/provider.example.com/8080/series/user/secret/7.mkv", "/http/provider.example.com/8080/series/***/***/7.mkv")]
    [InlineData("/LIVE/user/secret/1.ts", "/LIVE/***/***/1.ts")]
    [InlineData("/timeshift/user/secret/60/2026-10-04:20-00/1.ts", "/timeshift/***/***/60/2026-10-04:20-00/1.ts")]
    public void RedactPath_RedactsTheCredentialsOfStreamPaths(string path, string expected)
    {
        Assert.Equal(expected, XtreamCredentialRedaction.RedactPath(path));
    }

    [Theory]
    [InlineData("/http/provider.example.com/8080/player_api.php")]
    [InlineData("/3/movie/603/recommendations")]
    [InlineData("/movie/42.mp4")]
    [InlineData("/movies/user/secret/42.mp4")]
    public void RedactPath_KeepsOtherPaths(string path)
    {
        Assert.Equal(path, XtreamCredentialRedaction.RedactPath(path));
    }

    [Fact]
    public void SanitizeText_RedactsTheCredentialsOfTheQueryAndOfTheStreamPath()
    {
        var sanitized = XtreamCredentialRedaction.SanitizeText(
            "Request to http://provider.example.com:8080/movie/user/secret/42.mp4?username=user&password=secret failed");

        Assert.Equal("Request to http://provider.example.com:8080/movie/***/***/42.mp4?username=***&password=*** failed", sanitized);
    }

    [Fact]
    public void RedactUri_RedactsTheCredentialsOfTheQueryAndOfTheStreamPath()
    {
        var redacted = XtreamCredentialRedaction.RedactUri(new Uri("http://provider.example.com:8080/movie/user/secret/42.mp4?username=user&password=secret&token=1"));

        Assert.Equal("http://provider.example.com:8080/movie/***/***/42.mp4?username=***&password=***&token=1", redacted.ToString());
    }

    [Fact]
    public void RedactServerRequest_SetsTheUrlTagsWithoutCredentials()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("xtreamforge.local", 5202);
        httpContext.Request.Path = "/movie/user/secret/42.mp4";
        httpContext.Request.QueryString = new QueryString("?username=user&password=secret");
        using var activity = new Activity("request");

        XtreamCredentialRedaction.RedactServerRequest(activity, httpContext.Request);

        Assert.Equal("/movie/***/***/42.mp4", activity.GetTagItem("url.path"));
        Assert.Equal("/movie/***/***/42.mp4?username=***&password=***", activity.GetTagItem("http.target"));
        Assert.DoesNotContain(activity.TagObjects, tag => tag.Value?.ToString()?.Contains(Password, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RedactClientRequest_SetsTheUrlTagsWithoutCredentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://provider.example.com:8080/series/user/secret/7.mkv");
        using var activity = new Activity("request");

        XtreamCredentialRedaction.RedactClientRequest(activity, request);

        Assert.Equal("http://provider.example.com:8080/series/***/***/7.mkv", activity.GetTagItem("url.full"));
        Assert.DoesNotContain(activity.TagObjects, tag => tag.Value?.ToString()?.Contains(Password, StringComparison.Ordinal) == true);
    }
}
