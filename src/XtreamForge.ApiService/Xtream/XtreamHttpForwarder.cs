using Microsoft.Net.Http.Headers;
using XtreamForge.ApiService.Options;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Xtream;

/// <summary>
/// HTTP exchange with the upstream provider: sends the incoming request upstream and copies an upstream response back
/// to the client, without the <c>Host</c> and hop-by-hop headers. The proxy routes only accept GET and HEAD,
/// so no request body is forwarded.
/// </summary>
public static class XtreamHttpForwarder
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

    /// <summary>
    /// Sends the incoming request of <paramref name="xtreamContext"/> to <paramref name="targetUri"/> through the Xtream HTTP client;
    /// the response is returned once its headers are read, so that its body can be streamed.
    /// </summary>
    public static async Task<HttpResponseMessage> SendAsync(IHttpClientFactory httpClientFactory, XtreamContext xtreamContext, Uri targetUri, CancellationToken cancellationToken)
    {
        using var requestMessage = CreateRequestMessage(targetUri, xtreamContext.Request);
        var httpClient = httpClientFactory.CreateClient(XtreamProxyOptions.HttpClientName);

        return await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public static HttpRequestMessage CreateRequestMessage(Uri targetUri, HttpRequest request)
    {
        var requestMessage = new HttpRequestMessage(new HttpMethod(request.Method), targetUri);

        var headersToSkip = GetHopByHopHeaders(request.Headers[HeaderNames.Connection]);
        headersToSkip.Add(HeaderNames.Host);

        // content headers are rejected here and dropped, as there is no request content
        foreach (var header in request.Headers.Where(header => !headersToSkip.Contains(header.Key)))
        {
            requestMessage.Headers.TryAddWithoutValidation(header.Key, [.. header.Value]);
        }

        // credentials of the incoming route, redacted from the telemetry and logs of the upstream request
        XtreamCredentialRedaction.SetPathCredentials(requestMessage, request);

        return requestMessage;
    }

    /// <summary>Copies the status, headers, and body (none for a HEAD request) of <paramref name="responseMessage"/> to <paramref name="response"/>.</summary>
    public static async Task WriteResponseAsync(HttpResponseMessage responseMessage, HttpResponse response, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)responseMessage.StatusCode;

        var headersToSkip = GetHopByHopHeaders(responseMessage.Headers.TryGetValues(HeaderNames.Connection, out var connection) ? connection : []);
        foreach (var header in responseMessage.Headers.Where(header => !headersToSkip.Contains(header.Key)))
        {
            response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in responseMessage.Content.Headers
            .Where(header => !headersToSkip.Contains(header.Key)
                && !header.Key.Equals(HeaderNames.ContentLength, StringComparison.OrdinalIgnoreCase)))
        {
            response.Headers[header.Key] = header.Value.ToArray();
        }

        // the body is copied unchanged, so its length is kept: players use it to show the duration and to seek, also for HEAD and 206.
        // It is null for a chunked upstream body, and for a body decompressed by the Xtream client, whose length is unknown
        response.ContentLength = responseMessage.Content.Headers.ContentLength;

        foreach (var headerName in headersToSkip)
        {
            response.Headers.Remove(headerName);
        }

        if (HttpMethods.IsHead(response.HttpContext.Request.Method))
        {
            return;
        }

        await response.StartAsync(cancellationToken);
        await using var responseStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
        await responseStream.CopyToAsync(response.Body, cancellationToken);
    }

    // the standard hop-by-hop headers and the ones listed by the Connection header
    private static HashSet<string> GetHopByHopHeaders(IEnumerable<string> connectionValues)
    {
        var headersToSkip = new HashSet<string>(HopByHopHeaders, StringComparer.OrdinalIgnoreCase);
        foreach (var token in connectionValues.SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            headersToSkip.Add(token);
        }

        return headersToSkip;
    }
}
