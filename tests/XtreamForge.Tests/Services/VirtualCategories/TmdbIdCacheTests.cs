using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.VirtualCategories;

public sealed class TmdbIdCacheTests : IDisposable
{
    private const string Key = "test";

    private readonly SteppingTimeProvider _time = new();
    private readonly TmdbIdCache _cache;

    public TmdbIdCacheTests()
    {
        _cache = new TmdbIdCache(_time);
    }

    [Fact]
    public async Task GetOrComputeAsync_WhenTheComputationFails_KeepsAnEmptySetForTheFailureDuration()
    {
        var computeCount = 0;
        Task<IReadOnlySet<long>?> Fail(CancellationToken _)
        {
            computeCount++;
            return Task.FromResult<IReadOnlySet<long>?>(null);
        }

        var first = await _cache.GetOrComputeAsync(Key, Fail, TestContext.Current.CancellationToken);
        _time.Now += TmdbIdCache.FailureDuration - TimeSpan.FromSeconds(1);
        var second = await _cache.GetOrComputeAsync(Key, Fail, TestContext.Current.CancellationToken);
        _time.Now += TimeSpan.FromSeconds(1);
        await _cache.GetOrComputeAsync(Key, Fail, TestContext.Current.CancellationToken);

        Assert.Empty(first);
        Assert.Empty(second);
        Assert.Equal(2, computeCount);
    }

    [Fact]
    public async Task GetOrComputeAsync_WhenTheRefreshFails_KeepsTheLastKnownIds()
    {
        await _cache.GetOrComputeAsync(Key, Compute(603), TestContext.Current.CancellationToken);
        _time.Now += TmdbIdCache.Duration;

        var afterFailure = await _cache.GetOrComputeAsync(Key, Compute(null), TestContext.Current.CancellationToken);
        _time.Now += TmdbIdCache.FailureDuration;
        var afterRecovery = await _cache.GetOrComputeAsync(Key, Compute(604), TestContext.Current.CancellationToken);

        Assert.Equal([603L], afterFailure);
        Assert.Equal([604L], afterRecovery);
    }

    [Fact]
    public async Task GetOrComputeAsync_WhenInvalidatedThenTheComputationFails_DoesNotReturnTheInvalidatedIds()
    {
        await _cache.GetOrComputeAsync(Key, Compute(603), TestContext.Current.CancellationToken);
        _cache.Invalidate(Key);

        var afterFailure = await _cache.GetOrComputeAsync(Key, Compute(null), TestContext.Current.CancellationToken);

        Assert.Empty(afterFailure);
    }

    [Fact]
    public async Task GetOrComputeAsync_WhileExpiredIdsAreComputedAgain_ReturnsTheLastKnownIdsWithoutWaiting()
    {
        await _cache.GetOrComputeAsync(Key, Compute(603), TestContext.Current.CancellationToken);
        _time.Now += TmdbIdCache.Duration;
        var refresh = new TaskCompletionSource<IReadOnlySet<long>?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var refreshing = _cache.GetOrComputeAsync(Key, _ => refresh.Task, TestContext.Current.CancellationToken);
        var duringRefresh = await _cache.GetOrComputeAsync(Key, Compute(999), TestContext.Current.CancellationToken);
        refresh.SetResult(new HashSet<long> { 604 });

        Assert.Equal([603L], duringRefresh);
        Assert.Equal([604L], await refreshing);
        Assert.Equal([604L], await _cache.GetOrComputeAsync(Key, Compute(999), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetOrComputeAsync_WhileTheFirstComputationRuns_WaitsForIt()
    {
        var compute = new TaskCompletionSource<IReadOnlySet<long>?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var computing = _cache.GetOrComputeAsync(Key, _ => compute.Task, TestContext.Current.CancellationToken);
        var waiting = _cache.GetOrComputeAsync(Key, Compute(999), TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        compute.SetResult(new HashSet<long> { 603 });

        Assert.Equal([603L], await computing);
        Assert.Equal([603L], await waiting);
    }

    public void Dispose() => _cache.Dispose();

    private static Func<CancellationToken, Task<IReadOnlySet<long>?>> Compute(long? tmdbId)
        => _ => Task.FromResult<IReadOnlySet<long>?>(tmdbId is { } id ? new HashSet<long> { id } : null);
}
