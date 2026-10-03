using XtreamForge.ApiService.Services;
using XtreamForge.Domain.Enums;

namespace XtreamForge.Tests.Services;

public class TmdbIdRetrieverQueueTests
{
    private readonly TmdbIdRetrieverQueue _queue = new();

    [Fact]
    public void TryEnqueue_WhenSameStreamIsAlreadyPending_ReturnsFalse()
    {
        Assert.True(_queue.TryEnqueue(CreateRequest(1, ContentType.Vod, "42")));
        Assert.False(_queue.TryEnqueue(CreateRequest(1, ContentType.Vod, "42")));
    }

    [Fact]
    public void TryEnqueue_WhenSourceOrContentTypeDiffers_AcceptsEachRequest()
    {
        Assert.True(_queue.TryEnqueue(CreateRequest(1, ContentType.Vod, "42")));
        Assert.True(_queue.TryEnqueue(CreateRequest(2, ContentType.Vod, "42")));
        Assert.True(_queue.TryEnqueue(CreateRequest(1, ContentType.Series, "42")));
    }

    [Fact]
    public void TryEnqueue_WhenContentTypeIsUndefined_ReturnsFalse()
    {
        Assert.False(_queue.TryEnqueue(CreateRequest(1, ContentType.Undefined, "42")));
    }

    [Fact]
    public void Complete_AllowsTheSameStreamToBeEnqueuedAgain()
    {
        var request = CreateRequest(1, ContentType.Vod, "42");
        _queue.TryEnqueue(request);

        _queue.Complete(request);

        Assert.True(_queue.TryEnqueue(request));
    }

    [Fact]
    public async Task ReadAllAsync_ReturnsEnqueuedRequestsInOrder()
    {
        _queue.TryEnqueue(CreateRequest(1, ContentType.Vod, "1"));
        _queue.TryEnqueue(CreateRequest(1, ContentType.Vod, "2"));

        var streamIds = new List<string>();
        await foreach (var request in _queue.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            streamIds.Add(request.StreamId);
            if (streamIds.Count == 2)
                break;
        }

        Assert.Equal(["1", "2"], streamIds);
    }

    private static TmdbIdRetrieverRequest CreateRequest(int xtreamSourceId, ContentType type, string streamId) =>
        new(xtreamSourceId, "http", "provider.example.com", 8080, "user", "secret", streamId, type);
}
