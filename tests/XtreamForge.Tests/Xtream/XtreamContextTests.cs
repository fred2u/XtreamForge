using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;

namespace XtreamForge.Tests.Xtream;

public class XtreamContextTests
{
    [Fact]
    public void BuildTargetUri_PreservesPathAndIncomingQuery()
    {
        var context = CreateContext("player_api.php", "?username=user&password=pass&action=get_vod_streams");

        var uri = context.BuildTargetUri();

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("provider.example.com", uri.Host);
        Assert.Equal(8443, uri.Port);
        Assert.Equal("/player_api.php", uri.AbsolutePath);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("user", query["username"].ToString());
        Assert.Equal("pass", query["password"].ToString());
        Assert.Equal("get_vod_streams", query["action"].ToString());
    }

    [Fact]
    public void BuildTargetUri_WithoutQuery_HasEmptyQuery()
    {
        var context = CreateContext("movie/user/pass/1.mp4", string.Empty);

        var uri = context.BuildTargetUri();

        Assert.Equal("/movie/user/pass/1.mp4", uri.AbsolutePath);
        Assert.Equal(string.Empty, uri.Query);
    }

    [Fact]
    public void BuildTargetUri_WithEmptyPath_TargetsRoot()
    {
        var context = CreateContext(string.Empty, string.Empty);

        var uri = context.BuildTargetUri();

        Assert.Equal("/", uri.AbsolutePath);
    }

    [Fact]
    public void BuildTargetUri_EscapesPathSegments()
    {
        var context = CreateContext("movie/a b/c?d/1.mp4", string.Empty);

        var uri = context.BuildTargetUri();

        Assert.Equal("/movie/a%20b/c%3Fd/1.mp4", uri.AbsolutePath);
        Assert.Equal(string.Empty, uri.Query);
    }

    [Fact]
    public void BuildTargetUri_WithQueryParameter_OverridesExistingValueAndKeepsOthers()
    {
        var context = CreateContext("player_api.php", "?username=user&action=get_vod_streams&category_id=5");

        var uri = context.BuildTargetUri(new KeyValuePair<string, string>("category_id", "42"));

        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal(3, query.Count);
        Assert.Equal("42", query["category_id"].ToString());
        Assert.Equal("user", query["username"].ToString());
        Assert.Equal("get_vod_streams", query["action"].ToString());
    }

    [Fact]
    public void BuildTargetUri_WithQueryParameters_AddsMissingParameters()
    {
        var context = CreateContext("player_api.php", "?username=user");

        var uri = context.BuildTargetUri(new Dictionary<string, string> { ["action"] = "get_series" });

        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("user", query["username"].ToString());
        Assert.Equal("get_series", query["action"].ToString());
    }

    private static XtreamContext CreateContext(string rest, string queryString)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString(queryString);
        return new XtreamContext("https", "provider.example.com", 8443, rest, httpContext, RequestAction.Undefined, ContentType.Undefined);
    }
}
