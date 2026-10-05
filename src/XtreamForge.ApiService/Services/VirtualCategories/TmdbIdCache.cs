using System.Collections.Concurrent;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.VirtualCategories;

/// <summary>
/// In-memory sets of TMDB IDs computed from TMDB (recommended movies, popular movies and TV shows), shared by the Xtream requests:
/// computing a set calls TMDB several times. Each set is identified by a key, kept for <see cref="Duration"/> (TMDB lists change slowly),
/// computed by one request at a time, and can be invalidated (the recommendations, when the watch history changes).
/// While a set is computed, the other requests get its last known value instead of waiting for TMDB; a failed computation keeps
/// the last known value (or an empty set) for <see cref="FailureDuration"/>, so that TMDB is not called and waited for on every request while it is down.
/// </summary>
public sealed class TmdbIdCache(TimeProvider timeProvider) : IDisposable
{
    public const string RecommendationsKey = "recommendations";

    public static readonly TimeSpan Duration = TimeSpan.FromHours(6);

    public static readonly TimeSpan FailureDuration = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public static string PopularKey(ContentType contentType) => $"popular:{contentType}";

    /// <summary>
    /// Returns the cached IDs of <paramref name="key"/>, or computes them with <paramref name="compute"/>;
    /// a null result (failure) is replaced by the last known IDs, or an empty set, kept for <see cref="FailureDuration"/>.
    /// </summary>
    public async Task<IReadOnlySet<long>> GetOrComputeAsync(string key, Func<CancellationToken, Task<IReadOnlySet<long>?>> compute, CancellationToken cancellationToken)
    {
        var entry = _entries.GetOrAdd(key, _ => new Entry());
        if (entry.TryGet(timeProvider.GetUtcNow()) is { } cached)
            return cached;

        if (!await entry.ComputeLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            // another request is computing the set: only a first computation is waited for
            if (entry.LastKnown is { } lastKnown)
                return lastKnown;

            await entry.ComputeLock.WaitAsync(cancellationToken);
        }

        try
        {
            if (entry.TryGet(timeProvider.GetUtcNow()) is { } computedMeanwhile)
                return computedMeanwhile;

            var version = entry.Version;
            var tmdbIds = await compute(cancellationToken);
            if (tmdbIds is null)
            {
                var fallback = entry.LastKnown ?? new HashSet<long>();
                entry.TrySet(fallback, version, timeProvider.GetUtcNow() + FailureDuration);

                return fallback;
            }

            // invalidated while computing: the result is returned but not kept
            entry.TrySet(tmdbIds, version, timeProvider.GetUtcNow() + Duration);

            return tmdbIds;
        }
        finally
        {
            entry.ComputeLock.Release();
        }
    }

    /// <summary>
    /// Forgets the cached IDs of <paramref name="key"/>, which are no longer valid (for example a movie just watched must not stay recommended):
    /// unlike expired IDs, they are not returned while computed again, nor after a failure.
    /// </summary>
    public void Invalidate(string key)
    {
        if (_entries.TryGetValue(key, out var entry))
            entry.Invalidate();
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            entry.ComputeLock.Dispose();
        }
    }

    private sealed class Entry
    {
        private readonly Lock _stateLock = new();

        // the last computed or fallback IDs, kept once expired (but not once invalidated) to answer while they are computed again
        private IReadOnlySet<long>? _tmdbIds;
        private DateTimeOffset _expiresAtUtc;
        private long _version;

        public SemaphoreSlim ComputeLock { get; } = new(1, 1);

        public long Version
        {
            get
            {
                lock (_stateLock)
                {
                    return _version;
                }
            }
        }

        public IReadOnlySet<long>? LastKnown
        {
            get
            {
                lock (_stateLock)
                {
                    return _tmdbIds;
                }
            }
        }

        public IReadOnlySet<long>? TryGet(DateTimeOffset now)
        {
            lock (_stateLock)
            {
                return _tmdbIds is not null && now < _expiresAtUtc ? _tmdbIds : null;
            }
        }

        public void TrySet(IReadOnlySet<long> tmdbIds, long version, DateTimeOffset expiresAtUtc)
        {
            lock (_stateLock)
            {
                if (version != _version)
                    return;

                _tmdbIds = tmdbIds;
                _expiresAtUtc = expiresAtUtc;
            }
        }

        public void Invalidate()
        {
            lock (_stateLock)
            {
                _version++;
                _tmdbIds = null;
            }
        }
    }
}
