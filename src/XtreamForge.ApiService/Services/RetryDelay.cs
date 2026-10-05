using System.Net;
using Polly;

namespace XtreamForge.ApiService.Services;

public static class RetryDelay
{
    /// <summary>Delay before the next attempt after a transient failure (see <see cref="IsTransient"/>).</summary>
    public static readonly TimeSpan TransientFailureDelay = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Returns the delay before the next attempt: <paramref name="firstDelay"/> after the first attempt, doubled on each new attempt, up to <paramref name="maximumDelay"/>.
    /// </summary>
    public static TimeSpan Get(int attemptCount, TimeSpan firstDelay, TimeSpan maximumDelay)
    {
        // the exponent is capped so that the doubling cannot overflow; the maximum delay is reached long before
        var delay = firstDelay * Math.Pow(2, Math.Clamp(attemptCount - 1, 0, 10));

        return delay < maximumDelay ? delay : maximumDelay;
    }

    /// <summary>
    /// Whether an upstream call may succeed shortly: network error, interrupted body, timeout, rejection by the resilience pipeline
    /// (open circuit), or an HTTP 408, 429, or 5xx status. To be called only when the caller did not cancel the operation.
    /// </summary>
    public static bool IsTransient(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: { } statusCode } => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500,
        HttpRequestException or IOException or OperationCanceledException or ExecutionRejectedException => true,
        _ => false
    };
}
