using XtreamForge.ApiService.Services.Queues;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Services;

/// <summary>
/// Queue of the playbacks to record in the watch history, fed by <see cref="TrackPlayback"/>.
/// A player sends several requests for one playback (range requests, seeks, reconnections): a request only starts a new playback
/// when no request of the same stream and account is running and the last one ended more than <see cref="PlaybackIdleTimeout"/> ago.
/// </summary>
public sealed class WatchHistoryQueue(TimeProvider timeProvider) : BackgroundQueue<WatchHistoryRequest>(QueueName, Capacity)
{
    public const string QueueName = "Watch history";

    public static readonly TimeSpan PlaybackIdleTimeout = TimeSpan.FromMinutes(30);

    private const int Capacity = 10000;

    private readonly Lock _gate = new();
    private readonly Dictionary<PlaybackKey, PlaybackState> _playbacks = [];

    /// <summary>
    /// Registers a running stream request until the returned handle is disposed, and enqueues a playback when the request starts a new one.
    /// </summary>
    public IDisposable TrackPlayback(string protocol, string host, int port, XtreamMovieStream movie)
    {
        var key = new PlaybackKey(protocol, host, port, movie);
        var now = timeProvider.GetUtcNow();

        bool isNewPlayback;
        lock (_gate)
        {
            RemoveEndedPlaybacks(now);

            isNewPlayback = !_playbacks.TryGetValue(key, out var state);
            if (state is null)
            {
                state = new PlaybackState();
                _playbacks[key] = state;
            }

            state.RunningRequests++;
        }

        if (isNewPlayback)
            TryEnqueue(new WatchHistoryRequest(protocol, host, port, movie, now));

        return new PlaybackRequest(this, key);
    }

    // the playbacks are already grouped by TrackPlayback: each request is a distinct playback
    protected override object GetKey(WatchHistoryRequest request) => request;

    private void EndRequest(PlaybackKey key)
    {
        var now = timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (!_playbacks.TryGetValue(key, out var state))
                return;

            state.RunningRequests--;
            state.LastRequestEndedAtUtc = now;
        }
    }

    // the playbacks are few (one per stream being watched), so they are all checked on each request
    private void RemoveEndedPlaybacks(DateTimeOffset now)
    {
        foreach (var (key, state) in _playbacks)
        {
            if (state.RunningRequests == 0 && now - state.LastRequestEndedAtUtc > PlaybackIdleTimeout)
                _playbacks.Remove(key);
        }
    }

    private readonly record struct PlaybackKey(string Protocol, string Host, int Port, XtreamMovieStream Movie);

    private sealed class PlaybackState
    {
        public int RunningRequests { get; set; }

        public DateTimeOffset LastRequestEndedAtUtc { get; set; }
    }

    private sealed class PlaybackRequest(WatchHistoryQueue queue, PlaybackKey key) : IDisposable
    {
        private int _isDisposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
                queue.EndRequest(key);
        }
    }
}
