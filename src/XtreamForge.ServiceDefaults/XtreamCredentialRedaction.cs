using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace XtreamForge.ServiceDefaults;

public static partial class XtreamCredentialRedaction
{
    private const string RedactedValue = "***";

    // the username and password segments of a stream path
    private const string RedactedPathCredentials = $"{RedactedValue}/{RedactedValue}";

    private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "username",
        "password"
    };

    /// <summary>
    /// Redacts the Xtream credentials of a text: the <c>username</c> / <c>password</c> query parameters (plain or URL-encoded)
    /// and the credentials of the stream paths (see <see cref="RedactPath"/>).
    /// </summary>
    public static string SanitizeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var sanitized = SensitiveQueryParameterPattern().Replace(value, $"${{1}}{RedactedValue}");
        sanitized = EncodedSensitiveQueryParameterPattern().Replace(sanitized, $"${{1}}{RedactedValue}");
        return RedactPath(sanitized);
    }

    /// <summary>
    /// Redacts the credentials of the Xtream stream paths, <c>{kind}/{username}/{password}/...</c> for the <c>movie</c>, <c>series</c>,
    /// <c>live</c>, and <c>timeshift</c> kinds, with or without upstream prefix.
    /// </summary>
    public static string RedactPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path ?? string.Empty;
        }

        return StreamPathCredentialsPattern().Replace(path, RedactedPathCredentials);
    }

    public static Uri RedactUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            return uri;
        }

        var redactedQuery = RedactQueryString(uri.Query);
        var uriBuilder = new UriBuilder(uri)
        {
            Path = RedactPath(uri.AbsolutePath),
            Query = redactedQuery
        };

        return uriBuilder.Uri;
    }

    public static void RedactServerRequest(Activity activity, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(request);

        var redactedUrl = SanitizeText(request.GetDisplayUrl());
        var redactedQuery = RedactQueryString(request.QueryString.Value);
        var redactedPath = RedactPath($"{request.PathBase}{request.Path}");
        var redactedTarget = BuildRequestTarget(redactedPath, redactedQuery);

        activity.SetTag("url.full", redactedUrl);
        activity.SetTag("http.url", redactedUrl);
        activity.SetTag("url.path", redactedPath);
        activity.SetTag("url.query", redactedQuery);
        activity.SetTag("http.target", redactedTarget);
    }

    public static void RedactClientRequest(Activity activity, HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestUri is null)
        {
            return;
        }

        var redactedUri = RedactUri(request.RequestUri);
        var redactedUrl = redactedUri.ToString();
        var redactedQuery = RedactQueryString(redactedUri.Query);
        var redactedTarget = BuildRequestTarget(redactedUri.AbsolutePath, redactedQuery);

        activity.SetTag("url.full", redactedUrl);
        activity.SetTag("http.url", redactedUrl);
        activity.SetTag("url.query", redactedQuery);
        activity.SetTag("http.target", redactedTarget);
    }

    public static string RedactQueryString(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString))
        {
            return string.Empty;
        }

        var query = queryString[0] == '?' ? queryString[1..] : queryString;
        if (string.IsNullOrEmpty(query))
        {
            return string.Empty;
        }

        var parsedQuery = QueryHelpers.ParseQuery($"?{query}");
        if (!parsedQuery.Keys.Any(SensitiveQueryKeys.Contains))
        {
            return query;
        }

        var queryBuilder = new QueryBuilder();
        foreach (var entry in parsedQuery)
        {
            foreach (var value in entry.Value)
            {
                queryBuilder.Add(
                    entry.Key,
                    SensitiveQueryKeys.Contains(entry.Key) ? RedactedValue : value ?? string.Empty);
            }
        }

        return queryBuilder.ToQueryString().Value is ['?', .. var redactedQuery]
            ? redactedQuery
            : string.Empty;
    }

    private static string BuildRequestTarget(string path, string redactedQuery) =>
        string.IsNullOrEmpty(redactedQuery)
            ? path
            : $"{path}?{redactedQuery}";

    [GeneratedRegex(@"((?:\?|&)(?:username|password)=)([^&#\s]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveQueryParameterPattern();

    [GeneratedRegex(@"((?:%3[fF]|%26)(?:username|password)(?:=|%3[dD]))(.*?)(?=(?:%26|%23|&|#|\s|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EncodedSensitiveQueryParameterPattern();

    // {kind}/{username}/{password}/ at the start of a path or after a slash; the stream file must follow
    [GeneratedRegex(@"(?<=(?:^|[/\s])(?:movie|series|live|timeshift)/)[^/?#\s]+/[^/?#\s]+(?=/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StreamPathCredentialsPattern();
}
