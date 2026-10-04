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

    [Fact]
    public void RedactRequestUri_RedactsThePathCredentialsOfTheIncomingRoute()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://provider.example.com:8080/user/secret/123");
        XtreamCredentialRedaction.SetPathCredentials(request, CreateRoutedRequest("/http/provider.example.com/8080/user/secret/123", "user", Password));

        var redacted = XtreamCredentialRedaction.RedactRequestUri(request);

        Assert.Equal("http://provider.example.com:8080/***/***/123", redacted?.ToString());
    }

    [Fact]
    public void RedactRequestUri_ComparesThePathSegmentsDecoded()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://provider.example.com:8080/us%20er/s%40cret/123");
        XtreamCredentialRedaction.SetPathCredentials(request, CreateRoutedRequest("/http/provider.example.com/8080/us er/s@cret/123", "us er", "s@cret"));

        var redacted = XtreamCredentialRedaction.RedactRequestUri(request);

        Assert.Equal("http://provider.example.com:8080/***/***/123", redacted?.ToString());
    }

    [Fact]
    public void RedactRequestUri_WithoutRouteCredentials_KeepsPathsWithoutStreamKind()
    {
        // a TMDB detail path has the shape of the short live form: only the route values identify credentials
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.themoviedb.org/3/movie/603");
        XtreamCredentialRedaction.SetPathCredentials(request, new DefaultHttpContext().Request);

        var redacted = XtreamCredentialRedaction.RedactRequestUri(request);

        Assert.Equal("https://api.themoviedb.org/3/movie/603", redacted?.ToString());
    }

    [Fact]
    public void RedactClientRequest_RedactsThePathCredentialsOfTheIncomingRoute()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://provider.example.com:8080/user/secret/123");
        XtreamCredentialRedaction.SetPathCredentials(request, CreateRoutedRequest("/http/provider.example.com/8080/user/secret/123", "user", Password));
        using var activity = new Activity("request");

        XtreamCredentialRedaction.RedactClientRequest(activity, request);

        Assert.Equal("http://provider.example.com:8080/***/***/123", activity.GetTagItem("url.full"));
        Assert.DoesNotContain(activity.TagObjects, tag => tag.Value?.ToString()?.Contains(Password, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RedactServerResponse_RedactsThePathCredentialsOfTheRouteValues()
    {
        var request = CreateRoutedRequest("/http/provider.example.com/8080/user/secret/123", "user", Password);
        using var activity = new Activity("request");
        XtreamCredentialRedaction.RedactServerRequest(activity, request);

        XtreamCredentialRedaction.RedactServerResponse(activity, request.HttpContext.Response);

        Assert.Equal("/http/provider.example.com/8080/***/***/123", activity.GetTagItem("url.path"));
        Assert.Equal("http://xtreamforge.local:5202/http/provider.example.com/8080/***/***/123", activity.GetTagItem("url.full"));
        Assert.DoesNotContain(activity.TagObjects, tag => tag.Value?.ToString()?.Contains(Password, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RedactServerResponse_WithoutRouteCredentials_KeepsTheTags()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/http/provider.example.com/8080/player_api.php";
        using var activity = new Activity("request");
        activity.SetTag("url.path", "kept");

        XtreamCredentialRedaction.RedactServerResponse(activity, httpContext.Response);

        Assert.Equal("kept", activity.GetTagItem("url.path"));
    }

    private static HttpRequest CreateRoutedRequest(string path, string username, string password)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("xtreamforge.local", 5202);
        httpContext.Request.Path = path;
        httpContext.Request.RouteValues[XtreamCredentialRedaction.UsernameRouteValue] = username;
        httpContext.Request.RouteValues[XtreamCredentialRedaction.PasswordRouteValue] = password;
        return httpContext.Request;
    }
}
