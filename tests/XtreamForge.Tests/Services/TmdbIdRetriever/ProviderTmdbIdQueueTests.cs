using XtreamForge.ApiService.Services.TmdbIdRetriever;
using XtreamForge.Domain.Enums;

namespace XtreamForge.Tests.Services.TmdbIdRetriever;

public class ProviderTmdbIdQueueTests
{
    private readonly ProviderTmdbIdQueue _queue = new();

    [Fact]
    public void TryEnqueue_AcceptsEveryBatchOfItems()
    {
        Assert.True(_queue.TryEnqueue(CreateRequest(ContentType.Vod, "1")));
        Assert.True(_queue.TryEnqueue(CreateRequest(ContentType.Vod, "1")));
        Assert.True(_queue.TryEnqueue(CreateRequest(ContentType.Series, "1")));
    }

    [Fact]
    public void TryEnqueue_WhenContentTypeIsUndefined_ReturnsFalse()
    {
        Assert.False(_queue.TryEnqueue(CreateRequest(ContentType.Undefined, "1")));
    }

    [Fact]
    public void TryEnqueue_WhenTheBatchIsEmpty_ReturnsFalse()
    {
        Assert.False(_queue.TryEnqueue(new ProviderTmdbIdRequest(1, ContentType.Vod, new Dictionary<string, long>())));
    }

    private static ProviderTmdbIdRequest CreateRequest(ContentType type, string streamId)
        => new(1, type, new Dictionary<string, long> { [streamId] = 603 });
}
