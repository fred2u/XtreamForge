using System.Net.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace XtreamForge.Api.Services;

public sealed class ForwarderService(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "XtreamForwarder";

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "Proxy-Connection",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade"
    };

    public async Task<IResult> ForwardAsync(Uri targetUri, HttpContext context)
    {
        using var requestMessage = CreateRequestMessage(targetUri, context.Request);
        var httpClient = httpClientFactory.CreateClient(HttpClientName);

        using var responseMessage = await httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted);

        CopyResponse(responseMessage, context.Response);

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return Results.Empty;
        }

        await context.Response.StartAsync(context.RequestAborted);
        await using var responseStream = await responseMessage.Content.ReadAsStreamAsync(context.RequestAborted);
        await responseStream.CopyToAsync(context.Response.Body, context.RequestAborted);

        return Results.Empty;
    }

    private static HttpRequestMessage CreateRequestMessage(Uri targetUri, HttpRequest request)
    {
        var requestMessage = new HttpRequestMessage(new HttpMethod(request.Method), targetUri);
        var hasBody = HasRequestBody(request);

        if (hasBody)
        {
            requestMessage.Content = new StreamContent(request.Body);
        }

        foreach (var header in request.Headers)
        {
            if (ShouldSkipRequestHeader(header.Key))
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

    private static bool HasRequestBody(HttpRequest request) =>
        request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding");

    private static bool ShouldSkipRequestHeader(string headerName) =>
        headerName.Equals(HeaderNames.Host, StringComparison.OrdinalIgnoreCase)
        || HopByHopHeaders.Contains(headerName);

    private static void CopyResponse(HttpResponseMessage responseMessage, HttpResponse response)
    {
        response.StatusCode = (int)responseMessage.StatusCode;

        foreach (var header in responseMessage.Headers)
        {
            response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in responseMessage.Content.Headers)
        {
            if (header.Key.Equals(HeaderNames.ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var hopByHopHeader in HopByHopHeaders)
        {
            response.Headers.Remove(hopByHopHeader);
        }
    }
}
