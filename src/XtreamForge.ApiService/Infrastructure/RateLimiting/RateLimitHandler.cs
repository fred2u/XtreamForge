using System.Net;

namespace XtreamForge.ApiService.Infrastructure.RateLimiting;

/// <summary>
/// Outermost handler of the upstream HTTP clients: waits for <see cref="UpstreamRateLimiter"/> before each request and,
/// on HTTP 429, slows the host down and sends a GET/HEAD request again, up to <see cref="MaximumAttempts"/> times in total.
/// The last 429 response is returned unchanged, so callers keep their usual behavior (the proxy forwards it).
/// </summary>
public sealed class RateLimitHandler(UpstreamRateLimiter rateLimiter, TimeProvider timeProvider) : DelegatingHandler
{
    public const int MaximumAttempts = 3;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = GetHost(request);
        // Requests without a body can be sent again; the Xtream proxy and the TMDB client only send GET and HEAD.
        var canRetry = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        var attempt = 1;
        while (true)
        {
            await rateLimiter.WaitAsync(host, cancellationToken);

            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                rateLimiter.OnSuccess(host);
                return response;
            }

            rateLimiter.OnRateLimited(host, GetRetryAfter(response));
            if (!canRetry || attempt >= MaximumAttempts)
            {
                return response;
            }

            response.Dispose();
            attempt++;
        }
    }

    // Scheme, host and port only: the path and query of Xtream requests contain credentials.
    private static string GetHost(HttpRequestMessage request) =>
        request.RequestUri?.GetLeftPart(UriPartial.Authority) ?? string.Empty;

    private TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;

        return retryAfter?.Date is { } date ? date - timeProvider.GetUtcNow() : null;
    }
}
