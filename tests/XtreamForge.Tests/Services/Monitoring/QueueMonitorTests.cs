using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Endpoints.Admin.Monitoring;
using XtreamForge.ApiService.Endpoints.Admin.Monitoring.Dto;
using XtreamForge.ApiService.Infrastructure.RateLimiting;
using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Monitoring;

public sealed class QueueMonitorTests : IDisposable
{
    private readonly SteppingTimeProvider _time = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly FakeQueue _queue = new("Lookups");
    private readonly QueueMonitor _monitor;

    public QueueMonitorTests()
    {
        _monitor = new QueueMonitor([_queue], _time, _meterFactory);
    }

    [Fact]
    public void GetSnapshots_BeforeAnySample_ReturnsTheCurrentSizeWithoutHistory()
    {
        _queue.Count = 12;

        var snapshot = Assert.Single(_monitor.GetSnapshots());

        Assert.Equal(("Lookups", 12), (snapshot.Name, snapshot.Size));
        Assert.Empty(snapshot.Samples);
    }

    [Fact]
    public void Sample_RecordsTheSizeAndTheItemsProcessedSinceThePreviousSample()
    {
        _queue.Count = 5;
        _monitor.RecordProcessed("Lookups", QueueItemOutcome.Succeeded);
        _monitor.RecordProcessed("Lookups", QueueItemOutcome.Succeeded);
        _monitor.RecordProcessed("Lookups", QueueItemOutcome.NoResult);
        _monitor.RecordProcessed("Lookups", QueueItemOutcome.Failed);
        _monitor.Sample();

        _time.Now += QueueMonitor.SampleInterval;
        _queue.Count = 3;
        _monitor.Sample();

        var samples = Assert.Single(_monitor.GetSnapshots()).Samples;
        Assert.Equal(
            [
                new QueueSample(_time.Now - QueueMonitor.SampleInterval, 5, 2, 1, 1),
                new QueueSample(_time.Now, 3, 0, 0, 0)
            ],
            samples);
    }

    [Fact]
    public void Sample_KeepsOneHourOfHistory()
    {
        var expectedLength = (int)(QueueMonitor.HistoryDuration / QueueMonitor.SampleInterval);

        for (var index = 0; index < expectedLength + 10; index++)
        {
            _queue.Count = index;
            _monitor.Sample();
            _time.Now += QueueMonitor.SampleInterval;
        }

        var samples = Assert.Single(_monitor.GetSnapshots()).Samples;
        Assert.Equal(expectedLength, samples.Count);
        Assert.Equal(10, samples[0].Size);
        Assert.Equal(expectedLength + 9, samples[^1].Size);
    }

    [Fact]
    public void QueueSizeGauge_ReportsTheCurrentSizeTaggedWithTheQueueName()
    {
        _queue.Count = 42;
        var measurements = new List<(int Value, object? Queue)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == QueueMonitor.MeterName && instrument.Name == "xtreamforge.queue.size")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((_, value, tags, _) => measurements.Add((value, tags[0].Value)));
        listener.Start();

        listener.RecordObservableInstruments();

        Assert.Contains((42, (object?)"Lookups"), measurements);
    }

    [Fact]
    public void QueuesGetEndpoint_ReturnsTheQueuesAndTheRateLimitedHosts()
    {
        _queue.Count = 7;
        _monitor.Sample();
        var rateLimiter = new UpstreamRateLimiter(_time, NullLogger<UpstreamRateLimiter>.Instance, _meterFactory);
        rateLimiter.OnRateLimited("https://api.themoviedb.org", null);
        var endpoint = new QueuesGetEndpoint(_monitor, rateLimiter);

        var result = endpoint.Get();

        var ok = Assert.IsType<Ok<QueuesStatusDto>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal(TimeSpan.FromSeconds(10), ok.Value.SampleInterval);
        var queue = Assert.Single(ok.Value.Queues);
        Assert.Equal(("Lookups", 7), (queue.Name, queue.Size));
        Assert.Equal(new QueueSampleDto(_time.Now, 7, 0, 0, 0), Assert.Single(queue.Samples));
        Assert.Equal(new RateLimitedHostDto("https://api.themoviedb.org", TimeSpan.FromSeconds(1), 1), Assert.Single(ok.Value.RateLimitedHosts));
    }

    public void Dispose()
    {
        _meterFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FakeQueue(string name) : IMonitoredQueue
    {
        public string Name { get; } = name;

        public int Count { get; set; }
    }
}
