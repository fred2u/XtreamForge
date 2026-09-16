using System.Collections.Concurrent;
using System.Threading.Channels;
using XtreamForge.Categories;

namespace XtreamForge.Xtream;

public sealed class TmdbResolutionQueue
{
    private static readonly TimeSpan DefaultRetryCooldown = TimeSpan.FromMinutes(30);
    private readonly Channel<TmdbResolutionRequest> _channel = Channel.CreateUnbounded<TmdbResolutionRequest>();
    private readonly ConcurrentDictionary<string, byte> _pendingKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _retryAfterUtc = new(StringComparer.Ordinal);
    private readonly TimeSpan _retryCooldown;

    public TmdbResolutionQueue()
        : this(DefaultRetryCooldown)
    {
    }

    public TmdbResolutionQueue(TimeSpan retryCooldown)
    {
        if (retryCooldown < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryCooldown), "Retry cooldown must be zero or greater.");
        }

        _retryCooldown = retryCooldown;
    }

    public bool TryEnqueue(TmdbResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = GetPendingKey(request);
        if (_retryAfterUtc.TryGetValue(key, out var retryAfterUtc)
            && retryAfterUtc > DateTimeOffset.UtcNow)
        {
            return false;
        }

        if (!_pendingKeys.TryAdd(key, 0))
        {
            return false;
        }

        if (_channel.Writer.TryWrite(request))
        {
            return true;
        }

        _pendingKeys.TryRemove(key, out _);
        return false;
    }

    public IAsyncEnumerable<TmdbResolutionRequest> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void MarkCompleted(TmdbResolutionRequest request, bool resolved)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = GetPendingKey(request);
        _pendingKeys.TryRemove(key, out _);

        if (resolved || _retryCooldown == TimeSpan.Zero)
        {
            _retryAfterUtc.TryRemove(key, out _);
            return;
        }

        _retryAfterUtc[key] = DateTimeOffset.UtcNow.Add(_retryCooldown);
    }

    private static string GetPendingKey(TmdbResolutionRequest request) =>
        $"{request.SourceId}:{request.ContentType}:{request.StreamId}";
}

public sealed record TmdbResolutionRequest(
    int SourceId,
    ContentType ContentType,
    string StreamId,
    string Protocol,
    string Host,
    int Port,
    string Rest,
    string Username,
    string Password);
