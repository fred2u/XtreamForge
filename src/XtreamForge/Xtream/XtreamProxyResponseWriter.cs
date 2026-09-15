using System.Net.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace XtreamForge.Xtream;

internal static class XtreamProxyResponseWriter
{
    public static async Task WriteAsync(HttpResponseMessage responseMessage, HttpResponse response, string requestMethod, CancellationToken cancellationToken)
    {
        var headersToSkip = GetHopByHopHeaders(responseMessage.Headers);

        response.StatusCode = (int)responseMessage.StatusCode;

        foreach (var header in responseMessage.Headers)
        {
            if (!headersToSkip.Contains(header.Key))
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in responseMessage.Content.Headers)
        {
            if (!headersToSkip.Contains(header.Key)
                && !header.Key.Equals(HeaderNames.ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }
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
        var headersToSkip = new HashSet<string>(ForwarderService.HopByHopHeaders, StringComparer.OrdinalIgnoreCase);

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
