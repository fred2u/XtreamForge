using System.Collections.Concurrent;
using System.Threading.Channels;
using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services;

/// <summary>
/// Bounded in-memory queue of TMDB metadata loads, deduplicated per content type and TMDB ID.
/// A request stays pending from <see cref="TryEnqueue"/> until <see cref="Complete"/> or until it is dropped because the queue is full.
/// </summary>
public sealed class TmdbInfoQueue : IMonitoredQueue
{
    public const string QueueName = "TMDB info loads";

    private const int Capacity = 50000;

    private readonly ConcurrentDictionary<TmdbInfoRequest, byte> _pendingRequests = new();
    private readonly Channel<TmdbInfoRequest> _channel;

    public TmdbInfoQueue()
    {
        _channel = Channel.CreateBounded<TmdbInfoRequest>(
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

    public bool TryEnqueue(TmdbInfoRequest request)
    {
        if (request.Type is not (ContentType.Vod or ContentType.Series) || request.TmdbId <= 0)
            return false;

        if (!_pendingRequests.TryAdd(request, 0))
            return false;

        if (_channel.Writer.TryWrite(request))
            return true;

        _pendingRequests.TryRemove(request, out _);
        return false;
    }

    public IAsyncEnumerable<TmdbInfoRequest> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Marks a request as processed so that the same TMDB ID can be enqueued again.
    /// </summary>
    public void Complete(TmdbInfoRequest request)
        => _pendingRequests.TryRemove(request, out _);
}
