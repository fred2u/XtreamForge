using System.Collections.Concurrent;
using System.Threading.Channels;
using XtreamForge.ApiService.Services.Monitoring;

namespace XtreamForge.ApiService.Services.Queues;

/// <summary>
/// Bounded in-memory queue of background requests, monitored by <see cref="QueueMonitor"/> and consumed by a
/// <see cref="QueueBackgroundService{TRequest, TProcessor}"/>; when the queue is full, the oldest request is dropped.
/// A request is pending from <see cref="TryEnqueue"/> until it is processed (<see cref="Complete"/>) or dropped:
/// while it is pending, a request with the same <see cref="GetKey">key</see> is not enqueued.
/// </summary>
public abstract class BackgroundQueue<TRequest> : IMonitoredQueue where TRequest : notnull
{
    private readonly ConcurrentDictionary<object, byte> _pendingRequests = new();
    private readonly Channel<TRequest> _channel;

    protected BackgroundQueue(string name, int capacity)
    {
        Name = name;
        _channel = Channel.CreateBounded<TRequest>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,
                SingleWriter = false
            },
            Complete);
    }

    public string Name { get; }

    public int Count => _channel.Reader.Count;

    /// <summary>Returns false when the request is not accepted, when a request with the same key is pending, or when it cannot be written.</summary>
    public bool TryEnqueue(TRequest request)
    {
        if (!Accepts(request))
            return false;

        var key = GetKey(request);
        if (!_pendingRequests.TryAdd(key, 0))
            return false;

        if (_channel.Writer.TryWrite(request))
            return true;

        _pendingRequests.TryRemove(key, out _);
        return false;
    }

    public IAsyncEnumerable<TRequest> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Marks a request as processed so that a request with the same key can be enqueued again.</summary>
    public void Complete(TRequest request)
        => _pendingRequests.TryRemove(GetKey(request), out _);

    /// <summary>Identity of a request in the queue (compared with <see cref="object.Equals(object)"/>).</summary>
    protected abstract object GetKey(TRequest request);

    /// <summary>False for a request that cannot be processed, which is not enqueued.</summary>
    protected virtual bool Accepts(TRequest request) => true;
}
