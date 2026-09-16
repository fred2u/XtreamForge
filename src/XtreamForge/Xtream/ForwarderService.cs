using System.Net;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.Xtream;

public sealed class ForwarderService(
    XtreamUpstreamClient upstreamClient,
    ILogger<ForwarderService> logger)
{
    internal static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        Microsoft.Net.Http.Headers.HeaderNames.Connection,
        Microsoft.Net.Http.Headers.HeaderNames.KeepAlive,
        Microsoft.Net.Http.Headers.HeaderNames.ProxyAuthenticate,
        Microsoft.Net.Http.Headers.HeaderNames.ProxyAuthorization,
        "Proxy-Connection",
        Microsoft.Net.Http.Headers.HeaderNames.TE,
        Microsoft.Net.Http.Headers.HeaderNames.Trailer,
        Microsoft.Net.Http.Headers.HeaderNames.TransferEncoding,
        Microsoft.Net.Http.Headers.HeaderNames.Upgrade
    };

    public async Task<IResult> ForwardAsync(
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification,
        HttpContext context)
    {
        try
        {
            using var requestMessage = XtreamProxyHttpRequestFactory.Create(destination.TargetUri, context.Request);
            using var responseMessage = await upstreamClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            await XtreamProxyResponseWriter.WriteAsync(responseMessage, context.Response, context.Request.Method, context.RequestAborted);

            return Results.Empty;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                "Timed out forwarding Xtream request to upstream {Host}:{Port} for endpoint type {EndpointType} and action {Action}. ErrorType {ErrorType}. ErrorMessage {ErrorMessage}.",
                SanitizeForLog(destination.Host),
                destination.Port,
                classification.EndpointType,
                SanitizeForLog(classification.Action ?? "none"),
                exception.GetType().Name,
                XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                "Failed forwarding Xtream request to upstream {Host}:{Port} for endpoint type {EndpointType} and action {Action}. ErrorType {ErrorType}. ErrorMessage {ErrorMessage}.",
                SanitizeForLog(destination.Host),
                destination.Port,
                classification.EndpointType,
                SanitizeForLog(classification.Action ?? "none"),
                exception.GetType().Name,
                XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    internal static string SanitizeForLog(string value) =>
        value.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
}
