using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace XtreamForge.ServiceDefaults;

public static partial class XtreamCredentialRedaction
{
    /// <summary>Route values carrying the Xtream credentials in the path of an incoming request.</summary>
    public const string UsernameRouteValue = "username";
    public const string PasswordRouteValue = "password";

    private const string RedactedValue = "***";

    // the username and password segments of a stream path
    private const string RedactedPathCredentials = $"{RedactedValue}/{RedactedValue}";

    // credentials carried by the path of an upstream request, whatever its shape (see SetPathCredentials)
    private static readonly HttpRequestOptionsKey<IReadOnlyList<string>> PathCredentialsOption = new("XtreamForge.PathCredentials");

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

    /// <summary>
    /// Marks the credentials route values of the incoming request on its upstream request, so that <see cref="RedactRequestUri"/>
    /// also redacts the path segments that are not recognized by their shape (short live form <c>{username}/{password}/{streamId}</c>).
    /// </summary>
    public static void SetPathCredentials(HttpRequestMessage requestMessage, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(requestMessage);
        ArgumentNullException.ThrowIfNull(request);

        var credentials = GetRouteCredentials(request);
        if (credentials.Count > 0)
        {
            requestMessage.Options.Set(PathCredentialsOption, credentials);
        }
    }

    /// <summary>
    /// Redacts the URI of an upstream request: see <see cref="RedactUri"/>, plus the path segments marked by <see cref="SetPathCredentials"/>.
    /// </summary>
    public static Uri? RedactRequestUri(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestUri is null)
        {
            return null;
        }

        var redactedUri = RedactUri(request.RequestUri);
        if (!redactedUri.IsAbsoluteUri || !request.Options.TryGetValue(PathCredentialsOption, out var credentials))
        {
            return redactedUri;
        }

        return new UriBuilder(redactedUri) { Path = RedactPathSegments(redactedUri.AbsolutePath, credentials) }.Uri;
    }

    public static void RedactServerRequest(Activity activity, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(request);

        SetServerUrlTags(activity, request, RedactPath($"{request.PathBase}{request.Path}"));
    }

    /// <summary>
    /// Redacts the URL tags of a server span again once the request has been routed: the path segments matching the credentials
    /// route values (see <see cref="UsernameRouteValue"/>) are only known after routing, which follows the start of the span.
    /// </summary>
    public static void RedactServerResponse(Activity activity, HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(response);

        var request = response.HttpContext.Request;
        var credentials = GetRouteCredentials(request);
        if (credentials.Count == 0)
        {
            return;
        }

        SetServerUrlTags(activity, request, RedactPathSegments(RedactPath($"{request.PathBase}{request.Path}"), credentials));
    }

    public static void RedactClientRequest(Activity activity, HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(request);

        if (RedactRequestUri(request) is not { } redactedUri)
        {
            return;
        }

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

    private static void SetServerUrlTags(Activity activity, HttpRequest request, string redactedPath)
    {
        var redactedQuery = RedactQueryString(request.QueryString.Value);
        var redactedTarget = BuildRequestTarget(redactedPath, redactedQuery);
        var redactedUrl = $"{request.Scheme}://{request.Host.Value}{redactedTarget}";

        activity.SetTag("url.full", redactedUrl);
        activity.SetTag("http.url", redactedUrl);
        activity.SetTag("url.path", redactedPath);
        activity.SetTag("url.query", redactedQuery);
        activity.SetTag("http.target", redactedTarget);
    }

    private static List<string> GetRouteCredentials(HttpRequest request) =>
        [.. new[] { request.RouteValues[UsernameRouteValue], request.RouteValues[PasswordRouteValue] }
            .OfType<string>()
            .Where(value => value.Length > 0)];

    // the segments are compared decoded (upstream URI) and as is (incoming path, already decoded)
    private static string RedactPathSegments(string path, IReadOnlyList<string> credentials) =>
        string.Join('/', path.Split('/').Select(segment =>
            segment.Length > 0 && (credentials.Contains(segment) || credentials.Contains(Uri.UnescapeDataString(segment)))
                ? RedactedValue
                : segment));

    [GeneratedRegex(@"((?:\?|&)(?:username|password)=)([^&#\s]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveQueryParameterPattern();

    [GeneratedRegex(@"((?:%3[fF]|%26)(?:username|password)(?:=|%3[dD]))(.*?)(?=(?:%26|%23|&|#|\s|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EncodedSensitiveQueryParameterPattern();

    // {kind}/{username}/{password}/ at the start of a path or after a slash; the stream file must follow
    [GeneratedRegex(@"(?<=(?:^|[/\s])(?:movie|series|live|timeshift)/)[^/?#\s]+/[^/?#\s]+(?=/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StreamPathCredentialsPattern();
}
