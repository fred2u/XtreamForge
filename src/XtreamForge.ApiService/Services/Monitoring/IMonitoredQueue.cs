namespace XtreamForge.ApiService.Services.Monitoring;

/// <summary>
/// A background queue whose size is sampled by <see cref="QueueMonitor"/>. Register the queue singleton both as itself
/// and as <see cref="IMonitoredQueue"/>; its consumer reports each processed item with <see cref="QueueMonitor.RecordProcessed"/>.
/// </summary>
public interface IMonitoredQueue
{
    /// <summary>Stable name, displayed in the administration UI and used as the <c>queue</c> metric tag.</summary>
    string Name { get; }

    /// <summary>Number of items waiting to be processed.</summary>
    int Count { get; }
}

public enum QueueItemOutcome
{
    /// <summary>The item was processed and produced a result.</summary>
    Succeeded,

    /// <summary>The item was processed without producing a result (nothing to do, nothing found).</summary>
    NoResult,

    /// <summary>Processing the item failed.</summary>
    Failed
}
