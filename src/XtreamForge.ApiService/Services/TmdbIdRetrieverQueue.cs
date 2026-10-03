using System.Collections.Concurrent;
using System.Threading.Channels;
using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services;

/// <summary>
/// Bounded in-memory queue of TMDB ID lookups, deduplicated per source, content type, and stream.
/// A request stays pending from <see cref="TryEnqueue"/> until <see cref="Complete"/> or until it is dropped because the queue is full.
/// </summary>
public sealed class TmdbIdRetrieverQueue : IMonitoredQueue
{
    public const string QueueName = "TMDB ID lookups";

    private const int Capacity = 50000;

    private readonly ConcurrentDictionary<(int XtreamSourceId, ContentType Type, string StreamId), byte> _pendingRequests = new();
    private readonly Channel<TmdbIdRetrieverRequest> _channel;

    public TmdbIdRetrieverQueue()
    {
        _channel = Channel.CreateBounded<TmdbIdRetrieverRequest>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,
                SingleWriter = false
            },
            Complete);
    }

    public string Name => QueueName;

    public int Count => _channel.Reader.Count;

    public bool TryEnqueue(TmdbIdRetrieverRequest request)
    {
        if (request.Type is not (ContentType.Vod or ContentType.Series))
            return false;

        var key = GetKey(request);
        if (!_pendingRequests.TryAdd(key, 0))
            return false;

        if (_channel.Writer.TryWrite(request))
            return true;

        _pendingRequests.TryRemove(key, out _);
        return false;
    }

    public IAsyncEnumerable<TmdbIdRetrieverRequest> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Marks a request as processed so that the same stream can be enqueued again.
    /// </summary>
    public void Complete(TmdbIdRetrieverRequest request)
        => _pendingRequests.TryRemove(GetKey(request), out _);

    private static (int XtreamSourceId, ContentType Type, string StreamId) GetKey(TmdbIdRetrieverRequest request)
        => (request.XtreamSourceId, request.Type, request.StreamId);
}
