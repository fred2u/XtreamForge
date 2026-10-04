using System.Text.Json;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Xtream;

/// <summary>
/// Translates the failures of an upstream Xtream request (timeout, network error, broken body, invalid JSON) into the response
/// of the incoming request: <c>504</c> / <c>502</c> while the response has not started. Once started, its status can no longer change,
/// so the connection is aborted to tell the client that the body is truncated.
/// </summary>
public static class XtreamUpstreamFailure
{
    /// <summary>Whether <paramref name="exception"/> is an upstream failure, rather than the cancellation of the request by the client.</summary>
    public static bool IsUpstreamFailure(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException or JsonException or IOException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    public static IResult Handle(Exception exception, XtreamContext xtreamContext, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(xtreamContext);
        ArgumentNullException.ThrowIfNull(logger);

        var hasStarted = xtreamContext.Response.HasStarted;
        logger.LogWarning(
            exception,
            "Xtream request {Action} to {Protocol}://{Host}:{Port} failed (response started: {HasStarted}): {ErrorMessage}",
            xtreamContext.Action, xtreamContext.Protocol, xtreamContext.Host, xtreamContext.Port, hasStarted, XtreamCredentialRedaction.SanitizeText(exception.Message));

        if (hasStarted)
        {
            xtreamContext.Response.HttpContext.Abort();
            return Results.Empty;
        }

        return Results.StatusCode(exception is OperationCanceledException ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status502BadGateway);
    }
}
