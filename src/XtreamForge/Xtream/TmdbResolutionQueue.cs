using System.Collections.Concurrent;
using System.Threading.Channels;
using XtreamForge.Categories;

namespace XtreamForge.Xtream;

public sealed class TmdbResolutionQueue
{
    private readonly Channel<TmdbResolutionRequest> _channel = Channel.CreateUnbounded<TmdbResolutionRequest>();
    private readonly ConcurrentDictionary<string, byte> _pendingKeys = new(StringComparer.Ordinal);

    public bool TryEnqueue(TmdbResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_pendingKeys.TryAdd(GetPendingKey(request), 0))
        {
            return false;
        }

        if (_channel.Writer.TryWrite(request))
        {
            return true;
        }

        _pendingKeys.TryRemove(GetPendingKey(request), out _);
        return false;
    }

    public IAsyncEnumerable<TmdbResolutionRequest> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void MarkCompleted(TmdbResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _pendingKeys.TryRemove(GetPendingKey(request), out _);
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
