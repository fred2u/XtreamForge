using System.Threading.Channels;
using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Services;

/// <summary>
/// Bounded in-memory queue of the playbacks to record in the watch history.
/// A player sends several requests for one playback (range requests, seeks, reconnections): a request only starts a new playback
/// when no request of the same stream and account is running and the last one ended more than <see cref="PlaybackIdleTimeout"/> ago.
/// </summary>
public sealed class WatchHistoryQueue : IMonitoredQueue
{
    public const string QueueName = "Watch history";

    public static readonly TimeSpan PlaybackIdleTimeout = TimeSpan.FromMinutes(30);

    private const int Capacity = 10000;

    private readonly Lock _gate = new();
    private readonly Dictionary<PlaybackKey, PlaybackState> _playbacks = [];
    private readonly Channel<WatchHistoryRequest> _channel;
    private readonly TimeProvider _timeProvider;

    public WatchHistoryQueue(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _channel = Channel.CreateBounded<WatchHistoryRequest>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public string Name => QueueName;

    public int Count => _channel.Reader.Count;

    /// <summary>
    /// Registers a running stream request until the returned handle is disposed, and enqueues a playback when the request starts a new one.
    /// </summary>
    public IDisposable TrackPlayback(string protocol, string host, int port, XtreamMovieStream movie)
    {
        var key = new PlaybackKey(protocol, host, port, movie);
        var now = _timeProvider.GetUtcNow();

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
            _channel.Writer.TryWrite(new WatchHistoryRequest(protocol, host, port, movie, now));

        return new PlaybackRequest(this, key);
    }

    public IAsyncEnumerable<WatchHistoryRequest> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    private void EndRequest(PlaybackKey key)
    {
        var now = _timeProvider.GetUtcNow();

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
