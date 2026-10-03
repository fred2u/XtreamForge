using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using XtreamForge.ApiService.Services.Monitoring;

namespace XtreamForge.ApiService.Infrastructure.RateLimiting;

/// <summary>An upstream host that answered HTTP 429 at least once since the application started.</summary>
public sealed record RateLimitedHost(string Host, TimeSpan Interval, long RateLimitedCount);

/// <summary>
/// Adaptive spacing of the requests sent to each upstream host (TMDB, Xtream providers). Requests are not delayed until a
/// host answers HTTP 429; then its requests are paused (honoring <c>Retry-After</c>) and spaced by an interval that doubles
/// on each 429 and shrinks again after each response that is not rate limited. Each host has its own state.
/// </summary>
public sealed class UpstreamRateLimiter
{
    private const double RecoveryFactor = 0.9;

    private static readonly TimeSpan InitialInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumPause = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan NegligibleInterval = TimeSpan.FromMilliseconds(100);

    private readonly ConcurrentDictionary<string, HostState> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpstreamRateLimiter> _logger;

    public UpstreamRateLimiter(TimeProvider timeProvider, ILogger<UpstreamRateLimiter> logger, IMeterFactory meterFactory)
    {
        _timeProvider = timeProvider;
        _logger = logger;

        meterFactory.Create(QueueMonitor.MeterName).CreateObservableCounter(
            "xtreamforge.upstream.rate_limited",
            () => GetHosts().Select(host => new Measurement<long>(host.RateLimitedCount, new KeyValuePair<string, object?>("host", host.Host))),
            unit: "{response}",
            description: "HTTP 429 responses received from an upstream host.");
    }

    /// <summary>Hosts that answered HTTP 429 since the application started, with their current interval.</summary>
    public IReadOnlyList<RateLimitedHost> GetHosts() =>
        [.. _hosts.Select(pair =>
        {
            lock (pair.Value.Gate)
            {
                return new RateLimitedHost(pair.Key, pair.Value.Interval, pair.Value.RateLimitedCount);
            }
        })];

    /// <summary>Minimum delay currently kept between two requests to <paramref name="host"/>; zero while it does not rate limit.</summary>
    public TimeSpan GetInterval(string host)
    {
        if (!_hosts.TryGetValue(host, out var state))
            return TimeSpan.Zero;

        lock (state.Gate)
        {
            return state.Interval;
        }
    }

    /// <summary>Waits until a request may be sent to <paramref name="host"/>, and reserves its slot.</summary>
    public async Task WaitAsync(string host, CancellationToken cancellationToken)
    {
        var delay = Reserve(host);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _timeProvider, cancellationToken);
        }
    }

    /// <summary>Reserves the next request slot of <paramref name="host"/> and returns how long the caller must wait before using it.</summary>
    public TimeSpan Reserve(string host)
    {
        // Hosts that never rate limited have no state and are never delayed.
        if (!_hosts.TryGetValue(host, out var state))
            return TimeSpan.Zero;

        lock (state.Gate)
        {
            var now = _timeProvider.GetUtcNow();
            var start = state.NextRequestAt > now ? state.NextRequestAt : now;
            state.NextRequestAt = start + state.Interval;

            return start - now;
        }
    }

    /// <summary>Slows the following requests to <paramref name="host"/> down; <paramref name="retryAfter"/> is the delay it asked for, if any.</summary>
    public void OnRateLimited(string host, TimeSpan? retryAfter)
    {
        var state = _hosts.GetOrAdd(host, _ => new HostState());
        TimeSpan interval;
        TimeSpan pause;
        lock (state.Gate)
        {
            state.RateLimitedCount++;
            state.Interval = state.Interval == TimeSpan.Zero ? InitialInterval : Min(state.Interval * 2, MaximumInterval);
            interval = state.Interval;
            pause = Min(retryAfter is { } requested && requested > interval ? requested : interval, MaximumPause);

            var resumeAt = _timeProvider.GetUtcNow() + pause;
            if (resumeAt > state.NextRequestAt)
            {
                state.NextRequestAt = resumeAt;
            }
        }

        _logger.LogWarning("Rate limit reached on {Host} (HTTP 429): pausing its requests for {Pause}, then spacing them by {Interval}", host, pause, interval);
    }

    /// <summary>Speeds the following requests to <paramref name="host"/> up again after a response that was not rate limited.</summary>
    public void OnSuccess(string host)
    {
        if (!_hosts.TryGetValue(host, out var state))
            return;

        lock (state.Gate)
        {
            var recovered = state.Interval * RecoveryFactor;
            state.Interval = recovered < NegligibleInterval ? TimeSpan.Zero : recovered;
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private sealed class HostState
    {
        public Lock Gate { get; } = new();

        public TimeSpan Interval { get; set; }

        public DateTimeOffset NextRequestAt { get; set; }

        public long RateLimitedCount { get; set; }
    }
}
