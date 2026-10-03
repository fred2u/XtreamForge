using XtreamForge.ApiService.Services;
using XtreamForge.Domain.Enums;

namespace XtreamForge.Tests.Services;

public class TmdbInfoQueueTests
{
    private readonly TmdbInfoQueue _queue = new();

    [Fact]
    public void TryEnqueue_WhenSameTmdbIdIsAlreadyPending_ReturnsFalse()
    {
        Assert.True(_queue.TryEnqueue(new TmdbInfoRequest(ContentType.Vod, 603)));
        Assert.False(_queue.TryEnqueue(new TmdbInfoRequest(ContentType.Vod, 603)));
    }

    [Fact]
    public void TryEnqueue_WhenContentTypeDiffers_AcceptsEachRequest()
    {
        Assert.True(_queue.TryEnqueue(new TmdbInfoRequest(ContentType.Vod, 603)));
        Assert.True(_queue.TryEnqueue(new TmdbInfoRequest(ContentType.Series, 603)));
        Assert.Equal(2, _queue.Count);
    }

    [Theory]
    [InlineData(ContentType.Undefined, 603)]
    [InlineData(ContentType.Vod, 0)]
    [InlineData(ContentType.Vod, -1)]
    public void TryEnqueue_WhenRequestIsInvalid_ReturnsFalse(ContentType type, long tmdbId)
    {
        Assert.False(_queue.TryEnqueue(new TmdbInfoRequest(type, tmdbId)));
    }

    [Fact]
    public void Complete_AllowsTheSameTmdbIdToBeEnqueuedAgain()
    {
        var request = new TmdbInfoRequest(ContentType.Vod, 603);
        _queue.TryEnqueue(request);

        _queue.Complete(request);

        Assert.True(_queue.TryEnqueue(request));
    }
}
