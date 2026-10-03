namespace XtreamForge.Tests.Infrastructure;

/// <summary>
/// Time provider whose delays complete immediately: <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>
/// records the requested delay and moves the clock forward instead of waiting.
/// </summary>
public sealed class SteppingTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public List<TimeSpan> Delays { get; } = [];

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Delays.Add(dueTime);
        Now += dueTime;
        ThreadPool.QueueUserWorkItem(_ => callback(state));

        return new CompletedTimer();
    }

    private sealed class CompletedTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
