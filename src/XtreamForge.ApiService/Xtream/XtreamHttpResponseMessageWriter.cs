using Microsoft.Net.Http.Headers;
using System.Net.Http.Headers;

namespace XtreamForge.ApiService.Xtream;

public static class XtreamHttpResponseMessageWriter
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

    public static Task WriteAsJsonAsync(object? value, HttpResponse response, CancellationToken cancellationToken)
    {
        return response.WriteAsJsonAsync(value, cancellationToken);
    }

    public static async Task WriteResponseAsync(HttpResponseMessage responseMessage, HttpResponse response, string requestMethod, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)responseMessage.StatusCode;

        var headersToSkip = GetHopByHopHeaders(responseMessage.Headers);
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

        foreach (var headerName in headersToSkip)
        {
            response.Headers.Remove(headerName);
        }

        if (HttpMethods.IsHead(requestMethod))
        {
            return;
        }

        await response.StartAsync(cancellationToken);
        await using var responseStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
        await responseStream.CopyToAsync(response.Body, cancellationToken);
    }

    private static HashSet<string> GetHopByHopHeaders(HttpResponseHeaders headers)
    {
        var headersToSkip = new HashSet<string>(HopByHopHeaders, StringComparer.OrdinalIgnoreCase);

        if (headers.TryGetValues(HeaderNames.Connection, out var values))
        {
            foreach (var token in values.SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
            {
                headersToSkip.Add(token);
            }
        }

        return headersToSkip;
    }
}
