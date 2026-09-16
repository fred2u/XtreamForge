using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace XtreamForge.ServiceDefaults;

public static partial class XtreamCredentialRedaction
{
    private const string RedactedValue = "***";

    private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "username",
        "password"
    };

    public static string SanitizeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var sanitized = SensitiveQueryParameterPattern().Replace(value, $"${{1}}{RedactedValue}");
        return EncodedSensitiveQueryParameterPattern().Replace(sanitized, $"${{1}}{RedactedValue}");
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
        var redactedTarget = BuildRequestTarget(request.PathBase, request.Path, redactedQuery);

        activity.SetTag("url.full", redactedUrl);
        activity.SetTag("http.url", redactedUrl);
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

    private static string BuildRequestTarget(PathString pathBase, PathString path, string redactedQuery) =>
        BuildRequestTarget($"{pathBase}{path}", redactedQuery);

    private static string BuildRequestTarget(string path, string redactedQuery) =>
        string.IsNullOrEmpty(redactedQuery)
            ? path
            : $"{path}?{redactedQuery}";

    [GeneratedRegex(@"((?:\?|&)(?:username|password)=)([^&#\s]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveQueryParameterPattern();

    [GeneratedRegex(@"((?:%3[fF]|%26)(?:username|password)(?:=|%3[dD]))(.*?)(?=(?:%26|%23|&|#|\s|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EncodedSensitiveQueryParameterPattern();
}
