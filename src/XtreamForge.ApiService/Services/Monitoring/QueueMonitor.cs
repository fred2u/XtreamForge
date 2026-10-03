using System.Diagnostics.Metrics;

namespace XtreamForge.ApiService.Services.Monitoring;

/// <summary>One sample of a queue: its size at <paramref name="At"/> and the items processed since the previous sample.</summary>
public sealed record QueueSample(DateTimeOffset At, int Size, int Succeeded, int NoResult, int Failed);

public sealed record QueueSnapshot(string Name, int Size, IReadOnlyList<QueueSample> Samples);

/// <summary>
/// Keeps an in-memory history of the background queues (one sample every <see cref="SampleInterval"/> during
/// <see cref="HistoryDuration"/>, lost on restart) and publishes them as OpenTelemetry metrics.
/// </summary>
public sealed class QueueMonitor
{
    public const string MeterName = "XtreamForge.ApiService";

    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan HistoryDuration = TimeSpan.FromHours(1);

    private static readonly int HistoryLength = (int)(HistoryDuration / SampleInterval);

    private readonly IReadOnlyList<IMonitoredQueue> _queues;
    private readonly Dictionary<string, QueueState> _states;
    private readonly TimeProvider _timeProvider;
    private readonly Counter<long> _processedCounter;

    public QueueMonitor(IEnumerable<IMonitoredQueue> queues, TimeProvider timeProvider, IMeterFactory meterFactory)
    {
        _queues = [.. queues];
        _states = _queues.ToDictionary(queue => queue.Name, _ => new QueueState(), StringComparer.Ordinal);
        _timeProvider = timeProvider;

        var meter = meterFactory.Create(MeterName);
        meter.CreateObservableGauge(
            "xtreamforge.queue.size",
            () => _queues.Select(queue => new Measurement<int>(queue.Count, new KeyValuePair<string, object?>("queue", queue.Name))),
            unit: "{item}",
            description: "Items waiting in a background queue.");
        _processedCounter = meter.CreateCounter<long>(
            "xtreamforge.queue.items.processed",
            unit: "{item}",
            description: "Items processed by a background queue, by outcome.");
    }

    /// <summary>Reports an item processed by the consumer of the queue named <paramref name="queueName"/>.</summary>
    public void RecordProcessed(string queueName, QueueItemOutcome outcome)
    {
        _states[queueName].Record(outcome);
        _processedCounter.Add(
            1,
            new KeyValuePair<string, object?>("queue", queueName),
            new KeyValuePair<string, object?>("outcome", outcome.ToString()));
    }

    /// <summary>Adds a sample to the history of every queue; called every <see cref="SampleInterval"/>.</summary>
    public void Sample()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var queue in _queues)
        {
            _states[queue.Name].Sample(now, queue.Count);
        }
    }

    public IReadOnlyList<QueueSnapshot> GetSnapshots() =>
        [.. _queues.Select(queue => new QueueSnapshot(queue.Name, queue.Count, _states[queue.Name].GetSamples()))];

    private sealed class QueueState
    {
        private readonly Lock _gate = new();
        private readonly Queue<QueueSample> _samples = new();
        private int _succeeded;
        private int _noResult;
        private int _failed;

        public void Record(QueueItemOutcome outcome)
        {
            lock (_gate)
            {
                switch (outcome)
                {
                    case QueueItemOutcome.Succeeded:
                        _succeeded++;
                        break;
                    case QueueItemOutcome.NoResult:
                        _noResult++;
                        break;
                    default:
                        _failed++;
                        break;
                }
            }
        }

        public void Sample(DateTimeOffset at, int size)
        {
            lock (_gate)
            {
                _samples.Enqueue(new QueueSample(at, size, _succeeded, _noResult, _failed));
                _succeeded = 0;
                _noResult = 0;
                _failed = 0;

                while (_samples.Count > HistoryLength)
                {
                    _samples.Dequeue();
                }
            }
        }

        public IReadOnlyList<QueueSample> GetSamples()
        {
            lock (_gate)
            {
                return [.. _samples];
            }
        }
    }
}
