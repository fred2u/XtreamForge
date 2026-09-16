using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.Tests;

public sealed class XtreamCredentialRedactionTests
{
    [Fact]
    public void SanitizeText_RedactsXtreamCredentialsInsideUrls()
    {
        var value = "GET /https/example.com/443/player_api.php?username=test-user&******";

        var sanitized = XtreamCredentialRedaction.SanitizeText(value);

        Assert.DoesNotContain("test-user", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("test-password", sanitized, StringComparison.Ordinal);
        Assert.Contains("username=REDACTED", sanitized, StringComparison.Ordinal);
        Assert.Contains("******", sanitized, StringComparison.Ordinal);
        Assert.Contains("action=get_vod_categories", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactUri_PreservesNonSensitiveQueryValues()
    {
        var uri = new Uri("https://example.com/player_api.php?username=test-user&******");

        var redacted = XtreamCredentialRedaction.RedactUri(uri);

        Assert.Equal("https://example.com/player_api.php?username=REDACTED&******", redacted.ToString());
    }

    [Fact]
    public void RedactServerRequest_UpdatesDiagnosticUrlTags()
    {
        using var activity = new Activity("incoming-request");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");
        context.Request.Path = "/https/example.com/443/player_api.php";
        context.Request.QueryString = new QueryString("?username=test-user&******");

        activity.Start();
        XtreamCredentialRedaction.RedactServerRequest(activity, context.Request);

        Assert.Equal(
            "https://example.com/https/example.com/443/player_api.php?username=REDACTED&******",
            activity.GetTagItem("url.full"));
        Assert.Equal(
            "/https/example.com/443/player_api.php?username=REDACTED&******",
            activity.GetTagItem("http.target"));
        Assert.Equal(
            "username=REDACTED&******",
            activity.GetTagItem("url.query"));
    }

    [Fact]
    public void RedactClientRequest_UpdatesOutgoingDiagnosticUrlTags()
    {
        using var activity = new Activity("outgoing-request");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://example.com/player_api.php?username=test-user&******");

        activity.Start();
        XtreamCredentialRedaction.RedactClientRequest(activity, request);

        Assert.Equal(
            "https://example.com/player_api.php?username=REDACTED&******",
            activity.GetTagItem("url.full"));
        Assert.Equal(
            "/player_api.php?username=REDACTED&******",
            activity.GetTagItem("http.target"));
        Assert.Equal(
            "username=REDACTED&******",
            activity.GetTagItem("url.query"));
    }
}
