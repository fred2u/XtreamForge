using XtreamForge.Categories;
using XtreamForge.Xtream;

namespace XtreamForge.Tests;

public sealed class TmdbResolutionQueueTests
{
    [Fact]
    public void TryEnqueue_DeduplicatesPendingRequests_BySourceContentTypeAndStreamId()
    {
        var queue = new TmdbResolutionQueue(TimeSpan.FromMinutes(1));
        var request = CreateRequest();

        Assert.True(queue.TryEnqueue(request));
        Assert.False(queue.TryEnqueue(request));

        queue.MarkCompleted(request, resolved: true);

        Assert.True(queue.TryEnqueue(request));
    }

    [Fact]
    public async Task TryEnqueue_AppliesCooldownAfterUnresolvedAttempt_AndAllowsRetryAfterCooldown()
    {
        var queue = new TmdbResolutionQueue(TimeSpan.FromMilliseconds(50));
        var request = CreateRequest();

        Assert.True(queue.TryEnqueue(request));

        queue.MarkCompleted(request, resolved: false);

        Assert.False(queue.TryEnqueue(request));

        await Task.Delay(100);

        Assert.True(queue.TryEnqueue(request));
    }

    private static TmdbResolutionRequest CreateRequest(
        int sourceId = 1,
        ContentType contentType = ContentType.Vod,
        string streamId = "123") =>
        new(
            sourceId,
            contentType,
            streamId,
            "https",
            "example.com",
            443,
            "player_api.php",
            "user",
            "password");
}
