using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Infrastructure.RateLimiting;

namespace XtreamForge.Tests.Infrastructure.RateLimiting;

public sealed class UpstreamRateLimiterTests : IDisposable
{
    private const string Host = "https://api.themoviedb.org";
    private const string OtherHost = "http://provider.example.com:8080";

    private readonly SteppingTimeProvider _time = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly UpstreamRateLimiter _limiter;

    public UpstreamRateLimiterTests()
    {
        _limiter = new UpstreamRateLimiter(_time, NullLogger<UpstreamRateLimiter>.Instance, _meterFactory);
    }

    [Fact]
    public void Reserve_WhenNeverRateLimited_DoesNotDelay()
    {
        Assert.Equal(TimeSpan.Zero, _limiter.Reserve(Host));
        Assert.Equal(TimeSpan.Zero, _limiter.Reserve(Host));
        Assert.Equal(TimeSpan.Zero, _limiter.GetInterval(Host));
    }

    [Fact]
    public void OnRateLimited_PausesTheNextRequestAndDoublesTheIntervalEachTime()
    {
        _limiter.OnRateLimited(Host, null);
        Assert.Equal(TimeSpan.FromSeconds(1), _limiter.GetInterval(Host));
        Assert.Equal(TimeSpan.FromSeconds(1), _limiter.Reserve(Host));

        _limiter.OnRateLimited(Host, null);
        Assert.Equal(TimeSpan.FromSeconds(2), _limiter.GetInterval(Host));
    }

    [Fact]
    public void OnRateLimited_CapsTheInterval()
    {
        for (var index = 0; index < 10; index++)
        {
            _limiter.OnRateLimited(Host, null);
        }

        Assert.Equal(TimeSpan.FromSeconds(30), _limiter.GetInterval(Host));
    }

    [Fact]
    public void OnRateLimited_HonorsRetryAfterWhenLongerThanTheInterval()
    {
        _limiter.OnRateLimited(Host, TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), _limiter.Reserve(Host));
    }

    [Fact]
    public void OnRateLimited_CapsRetryAfter()
    {
        _limiter.OnRateLimited(Host, TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromMinutes(1), _limiter.Reserve(Host));
    }

    [Fact]
    public void Reserve_SpacesConsecutiveRequestsByTheInterval()
    {
        _limiter.OnRateLimited(Host, null);
        _time.Now += TimeSpan.FromSeconds(1);

        var first = _limiter.Reserve(Host);
        var second = _limiter.Reserve(Host);

        Assert.Equal(TimeSpan.Zero, first);
        Assert.Equal(TimeSpan.FromSeconds(1), second);
    }

    [Fact]
    public void OnSuccess_ReducesTheIntervalUntilItIsRemoved()
    {
        _limiter.OnRateLimited(Host, null);

        _limiter.OnSuccess(Host);
        Assert.Equal(TimeSpan.FromMilliseconds(900), _limiter.GetInterval(Host));

        for (var index = 0; index < 100; index++)
        {
            _limiter.OnSuccess(Host);
        }

        Assert.Equal(TimeSpan.Zero, _limiter.GetInterval(Host));
    }

    [Fact]
    public void GetHosts_ListsRateLimitedHostsWithTheirIntervalAndCount()
    {
        Assert.Empty(_limiter.GetHosts());

        _limiter.OnRateLimited(Host, null);
        _limiter.OnRateLimited(Host, null);
        _limiter.OnSuccess(OtherHost);

        var host = Assert.Single(_limiter.GetHosts());
        Assert.Equal(new RateLimitedHost(Host, TimeSpan.FromSeconds(2), 2), host);
    }

    [Fact]
    public void OnRateLimited_DoesNotSlowOtherHostsDown()
    {
        _limiter.OnRateLimited(Host, TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.Zero, _limiter.Reserve(OtherHost));
        Assert.Equal(TimeSpan.Zero, _limiter.GetInterval(OtherHost));
    }

    [Fact]
    public async Task WaitAsync_WhenCancelled_GivesItsSlotBack()
    {
        _limiter.OnRateLimited(Host, TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _limiter.WaitAsync(Host, new CancellationToken(canceled: true)));

        Assert.Equal(TimeSpan.FromSeconds(10), _limiter.Reserve(Host));
    }

    [Fact]
    public async Task WaitAsync_WhenCancelledBeforeALaterReservation_KeepsTheLaterSlot()
    {
        // the wait only ends when cancelled
        var limiter = new UpstreamRateLimiter(new FrozenTimeProvider(), NullLogger<UpstreamRateLimiter>.Instance, _meterFactory);
        limiter.OnRateLimited(Host, TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();

        var cancelledWait = limiter.WaitAsync(Host, cancellation.Token);
        var laterDelay = limiter.Reserve(Host);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWait);

        Assert.Equal(TimeSpan.FromSeconds(11), laterDelay);
        Assert.Equal(TimeSpan.FromSeconds(12), limiter.Reserve(Host));
    }

    public void Dispose()
    {
        _meterFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Time provider whose clock does not move and whose timers never fire.</summary>
    private sealed class FrozenTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new PendingTimer();

        private sealed class PendingTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
