using System.Net;
using System.Net.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace XtreamForge.Api.Services;

public sealed class ForwarderService(
    IHttpClientFactory httpClientFactory,
    ILogger<ForwarderService> logger)
{
    public const string HttpClientName = "XtreamForwarder";

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

    public async Task<IResult> ForwardAsync(
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification,
        HttpContext context)
    {
        try
        {
            using var requestMessage = CreateRequestMessage(destination.TargetUri, context.Request);
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                exception,
                "Timed out forwarding Xtream request to upstream {Host}:{Port} for endpoint type {EndpointType} and action {Action}.",
                SanitizeForLog(destination.Host),
                destination.Port,
                classification.EndpointType,
                SanitizeForLog(classification.Action ?? "none"));

            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Failed forwarding Xtream request to upstream {Host}:{Port} for endpoint type {EndpointType} and action {Action}.",
                SanitizeForLog(destination.Host),
                destination.Port,
                classification.EndpointType,
                SanitizeForLog(classification.Action ?? "none"));

            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    private static HttpRequestMessage CreateRequestMessage(Uri targetUri, HttpRequest request)
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

    private static void CopyResponse(HttpResponseMessage responseMessage, HttpResponse response)
    {
        var headersToSkip = GetHopByHopHeaders(responseMessage.Headers);

        response.StatusCode = (int)responseMessage.StatusCode;

        foreach (var header in responseMessage.Headers)
        {
            if (headersToSkip.Contains(header.Key))
            {
                continue;
            }

            response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in responseMessage.Content.Headers)
        {
            if (headersToSkip.Contains(header.Key)
                || header.Key.Equals(HeaderNames.ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var headerName in headersToSkip)
        {
            response.Headers.Remove(headerName);
        }
    }

    private static HashSet<string> GetHopByHopHeaders(IHeaderDictionary headers)
    {
        var headersToSkip = new HashSet<string>(HopByHopHeaders, StringComparer.OrdinalIgnoreCase);
        AddConnectionHeaderTokens(headers[HeaderNames.Connection], headersToSkip);
        return headersToSkip;
    }

    private static HashSet<string> GetHopByHopHeaders(HttpResponseHeaders headers)
    {
        var headersToSkip = new HashSet<string>(HopByHopHeaders, StringComparer.OrdinalIgnoreCase);

        if (headers.TryGetValues(HeaderNames.Connection, out var values))
        {
            AddConnectionHeaderTokens(values, headersToSkip);
        }

        return headersToSkip;
    }

    private static void AddConnectionHeaderTokens(IEnumerable<string> values, HashSet<string> headersToSkip)
    {
        foreach (var token in values.SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            headersToSkip.Add(token);
        }
    }

    private static string SanitizeForLog(string value) =>
        value.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
}
