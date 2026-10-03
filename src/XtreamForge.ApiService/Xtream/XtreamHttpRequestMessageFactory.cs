using Microsoft.Net.Http.Headers;

namespace XtreamForge.ApiService.Xtream;

/// <summary>
/// Builds the upstream request of a proxied Xtream request. The proxy route only accepts GET and HEAD,
/// so no request body is forwarded.
/// </summary>
public static class XtreamHttpRequestMessageFactory
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        HeaderNames.Connection,
        HeaderNames.KeepAlive,
        HeaderNames.ProxyAuthenticate,
        HeaderNames.ProxyAuthorization,
        "Proxy-Connection",
        HeaderNames.TE,
        HeaderNames.Trailer,
        HeaderNames.TransferEncoding,
        HeaderNames.Upgrade
    };

    public static HttpRequestMessage Create(Uri targetUri, HttpRequest request)
    {
        var requestMessage = new HttpRequestMessage(new HttpMethod(request.Method), targetUri);

        var headersToSkip = GetHopByHopHeaders(request.Headers);

        foreach (var header in request.Headers)
        {
            if (ShouldSkipRequestHeader(header.Key, headersToSkip))
                continue;

            // content headers are rejected here and dropped, as there is no request content
            requestMessage.Headers.TryAddWithoutValidation(header.Key, [.. header.Value]);
        }

        return requestMessage;
    }

    private static bool ShouldSkipRequestHeader(string headerName, HashSet<string> headersToSkip)
    {
        return headerName.Equals(HeaderNames.Host, StringComparison.OrdinalIgnoreCase)
            || headersToSkip.Contains(headerName);
    }

    private static HashSet<string> GetHopByHopHeaders(IHeaderDictionary headers)
    {
        var headersToSkip = new HashSet<string>(HopByHopHeaders, StringComparer.OrdinalIgnoreCase);
        AddConnectionHeaderTokens(headers[HeaderNames.Connection], headersToSkip);
        return headersToSkip;
    }

    private static void AddConnectionHeaderTokens(IEnumerable<string> values, HashSet<string> headersToSkip)
    {
        foreach (var token in values.SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            headersToSkip.Add(token);
        }
    }
}
