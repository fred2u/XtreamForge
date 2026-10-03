using Microsoft.Extensions.Http.Logging;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Infrastructure;

/// <summary>
/// Request logging of the Xtream HTTP client. It replaces the default <see cref="IHttpClientFactory"/> logging, which writes the
/// request URL, while Xtream URLs carry the credentials in their query string or, for the streams, in their path.
/// </summary>
public sealed class XtreamHttpClientLogger(ILogger<XtreamHttpClientLogger> logger) : IHttpClientLogger
{
    public object? LogRequestStart(HttpRequestMessage request)
    {
        logger.LogInformation("Sending HTTP request {Method} {Uri}", request.Method, Redact(request));

        return null;
    }

    public void LogRequestStop(object? context, HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed)
        => logger.LogInformation(
            "Received HTTP response headers of {Method} {Uri} after {ElapsedMilliseconds}ms - {StatusCode}",
            request.Method, Redact(request), elapsed.TotalMilliseconds, (int)response.StatusCode);

    // the exception is logged, redacted, by the caller
    public void LogRequestFailed(object? context, HttpRequestMessage request, HttpResponseMessage? response, Exception exception, TimeSpan elapsed)
        => logger.LogInformation(
            "HTTP request {Method} {Uri} failed after {ElapsedMilliseconds}ms",
            request.Method, Redact(request), elapsed.TotalMilliseconds);

    private static string Redact(HttpRequestMessage request)
        => request.RequestUri is { } uri ? XtreamCredentialRedaction.RedactUri(uri).ToString() : string.Empty;
}
