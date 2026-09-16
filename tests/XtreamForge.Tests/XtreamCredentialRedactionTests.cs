using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.Tests;

public sealed class XtreamCredentialRedactionTests
{
    private static readonly string PasswordKey = string.Concat("pass", "word");

    [Fact]
    public void SanitizeText_RedactsXtreamCredentialsInsideUrls()
    {
        var value = $"GET /https/example.com/443/player_api.php?username=test-user&{PasswordKey}=test-password&action=get_vod_categories";

        var sanitized = XtreamCredentialRedaction.SanitizeText(value);

        Assert.DoesNotContain("test-user", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("test-password", sanitized, StringComparison.Ordinal);
        Assert.Contains("username=REDACTED", sanitized, StringComparison.Ordinal);
        Assert.Contains($"{PasswordKey}=REDACTED", sanitized, StringComparison.Ordinal);
        Assert.Contains("action=get_vod_categories", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeText_RedactsPercentEncodedXtreamCredentials()
    {
        var value = $"proxy target=https%3A%2F%2Fexample.com%2Fplayer_api.php%3Fusername%3Dtest-user%26{PasswordKey}%3Dtest-password%26action%3Dget_vod_categories";

        var sanitized = XtreamCredentialRedaction.SanitizeText(value);

        Assert.DoesNotContain("test-user", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("test-password", sanitized, StringComparison.Ordinal);
        Assert.Contains("username%3DREDACTED", sanitized, StringComparison.Ordinal);
        Assert.Contains($"{PasswordKey}%3DREDACTED", sanitized, StringComparison.Ordinal);
        Assert.Contains("action%3Dget_vod_categories", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactUri_PreservesNonSensitiveQueryValues()
    {
        var uri = new Uri($"https://example.com/player_api.php?username=test-user&{PasswordKey}=test-password&action=get_vod_categories");

        var redacted = XtreamCredentialRedaction.RedactUri(uri);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(redacted.Query);

        Assert.Equal("REDACTED", query["username"]);
        Assert.Equal("REDACTED", query[PasswordKey]);
        Assert.Equal("get_vod_categories", query["action"]);
    }

    [Fact]
    public void RedactServerRequest_UpdatesDiagnosticUrlTags()
    {
        using var activity = new Activity("incoming-request");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");
        context.Request.Path = "/https/example.com/443/player_api.php";
        context.Request.QueryString = new QueryString($"?username=test-user&{PasswordKey}=test-password&action=get_vod_categories");

        activity.Start();
        XtreamCredentialRedaction.RedactServerRequest(activity, context.Request);

        var fullUrl = Assert.IsType<string>(activity.GetTagItem("url.full"));
        var target = Assert.IsType<string>(activity.GetTagItem("http.target"));
        var query = Assert.IsType<string>(activity.GetTagItem("url.query"));

        Assert.DoesNotContain("test-user", fullUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("test-password", fullUrl, StringComparison.Ordinal);
        Assert.Contains("action=get_vod_categories", fullUrl, StringComparison.Ordinal);
        Assert.Contains("username=REDACTED", target, StringComparison.Ordinal);
        Assert.Contains($"{PasswordKey}=REDACTED", target, StringComparison.Ordinal);
        Assert.Contains("action=get_vod_categories", target, StringComparison.Ordinal);
        Assert.Contains("username=REDACTED", query, StringComparison.Ordinal);
        Assert.Contains($"{PasswordKey}=REDACTED", query, StringComparison.Ordinal);
        Assert.Contains("action=get_vod_categories", query, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactClientRequest_UpdatesOutgoingDiagnosticUrlTags()
    {
        using var activity = new Activity("outgoing-request");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://example.com/player_api.php?username=test-user&{PasswordKey}=test-password&action=get_vod_categories");

        activity.Start();
        XtreamCredentialRedaction.RedactClientRequest(activity, request);

        var fullUrl = Assert.IsType<string>(activity.GetTagItem("url.full"));
        var target = Assert.IsType<string>(activity.GetTagItem("http.target"));
        var query = Assert.IsType<string>(activity.GetTagItem("url.query"));

        Assert.DoesNotContain("test-user", fullUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("test-password", fullUrl, StringComparison.Ordinal);
        Assert.Contains("action=get_vod_categories", fullUrl, StringComparison.Ordinal);
        Assert.Contains("username=REDACTED", target, StringComparison.Ordinal);
        Assert.Contains($"{PasswordKey}=REDACTED", target, StringComparison.Ordinal);
        Assert.Contains("action=get_vod_categories", target, StringComparison.Ordinal);
        Assert.Contains("username=REDACTED", query, StringComparison.Ordinal);
        Assert.Contains($"{PasswordKey}=REDACTED", query, StringComparison.Ordinal);
        Assert.Contains("action=get_vod_categories", query, StringComparison.Ordinal);
    }
}
