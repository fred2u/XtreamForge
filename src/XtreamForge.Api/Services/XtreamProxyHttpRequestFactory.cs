using Microsoft.Net.Http.Headers;

namespace XtreamForge.Api.Services;

internal static class XtreamProxyHttpRequestFactory
{
    public static HttpRequestMessage Create(Uri targetUri, HttpRequest request)
    {
        var requestMessage = new HttpRequestMessage(new HttpMethod(request.Method), targetUri);
        var headersToSkip = GetHopByHopHeaders(request.Headers);

        if (ShouldCreateRequestContent(request))
        {
            requestMessage.Content = new StreamContent(request.Body);
        }

        foreach (var header in request.Headers)
        {
            if (ShouldSkipRequestHeader(header.Key, headersToSkip))
            {
                continue;
            }

            if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        return requestMessage;
    }

    private static bool ShouldCreateRequestContent(HttpRequest request) =>
        request.ContentLength is not null
        || request.Headers.ContainsKey(HeaderNames.TransferEncoding)
        || request.Headers.Any(static header =>
            header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase));

    private static bool ShouldSkipRequestHeader(string headerName, HashSet<string> headersToSkip) =>
        headerName.Equals(HeaderNames.Host, StringComparison.OrdinalIgnoreCase)
        || headersToSkip.Contains(headerName);

    private static HashSet<string> GetHopByHopHeaders(IHeaderDictionary headers)
    {
        var headersToSkip = new HashSet<string>(ForwarderService.HopByHopHeaders, StringComparer.OrdinalIgnoreCase);
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
