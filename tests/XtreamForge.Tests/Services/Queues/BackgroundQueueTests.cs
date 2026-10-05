using XtreamForge.ApiService.Services.Queues;

namespace XtreamForge.Tests.Services.Queues;

public class BackgroundQueueTests
{
    [Fact]
    public void TryEnqueue_WhenARequestWithTheSameKeyIsPending_ReturnsFalse()
    {
        var queue = new TestQueue(capacity: 10);

        Assert.True(queue.TryEnqueue(new TestRequest("a", 1)));
        Assert.False(queue.TryEnqueue(new TestRequest("a", 2)));
        Assert.True(queue.TryEnqueue(new TestRequest("b", 1)));
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void TryEnqueue_WhenTheRequestIsNotAccepted_ReturnsFalse()
    {
        var queue = new TestQueue(capacity: 10);

        Assert.False(queue.TryEnqueue(new TestRequest("", 1)));
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Complete_AllowsARequestWithTheSameKeyToBeEnqueuedAgain()
    {
        var queue = new TestQueue(capacity: 10);
        var request = new TestRequest("a", 1);
        queue.TryEnqueue(request);

        queue.Complete(request);

        Assert.True(queue.TryEnqueue(new TestRequest("a", 2)));
    }

    [Fact]
    public async Task TryEnqueue_WhenTheQueueIsFull_DropsAndCompletesTheOldestRequest()
    {
        var queue = new TestQueue(capacity: 1);
        queue.TryEnqueue(new TestRequest("a", 1));

        Assert.True(queue.TryEnqueue(new TestRequest("b", 1)));

        Assert.Equal(1, queue.Count);
        Assert.Equal("b", (await ReadAsync(queue)).Key);
        Assert.True(queue.TryEnqueue(new TestRequest("a", 2)));
    }

    private static async Task<TestRequest> ReadAsync(TestQueue queue)
    {
        await using var enumerator = queue.ReadAllAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        return enumerator.Current;
    }

    private sealed record TestRequest(string Key, int Value);

    // deduplicated by Key; a request without key is not accepted
    private sealed class TestQueue(int capacity) : BackgroundQueue<TestRequest>("Test", capacity)
    {
        protected override object GetKey(TestRequest request) => request.Key;

        protected override bool Accepts(TestRequest request) => request.Key.Length > 0;
    }
}
