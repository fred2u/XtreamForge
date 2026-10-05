using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.ApiService.Services.Queues;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Queues;

public sealed class QueueBackgroundServiceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TestMeterFactory _meterFactory = new();
    private readonly TestQueue _queue = new();
    private readonly QueueMonitor _monitor;
    private readonly TestProcessorBehavior _behavior = new();

    public QueueBackgroundServiceTests()
    {
        _monitor = new QueueMonitor([_queue], TimeProvider.System, _meterFactory);
    }

    [Theory]
    [InlineData("found", QueueItemOutcome.Succeeded)]
    [InlineData("missing", QueueItemOutcome.NoResult)]
    [InlineData("failing", QueueItemOutcome.Failed)]
    public async Task ExecuteAsync_ReportsTheOutcomeAndCompletesTheRequest(string request, QueueItemOutcome expectedOutcome)
    {
        await using var serviceProvider = CreateServiceProvider();
        using var backgroundService = CreateBackgroundService(serviceProvider, workerCount: 1);

        await backgroundService.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(_queue.TryEnqueue(request));
        var samples = await WaitForProcessedSamplesAsync(1);

        // the request is completed whatever the outcome: it can be enqueued again
        await WaitUntilAsync(() => _queue.TryEnqueue(request));
        await backgroundService.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            (expectedOutcome == QueueItemOutcome.Succeeded ? 1 : 0, expectedOutcome == QueueItemOutcome.NoResult ? 1 : 0, expectedOutcome == QueueItemOutcome.Failed ? 1 : 0),
            (samples.Sum(sample => sample.Succeeded), samples.Sum(sample => sample.NoResult), samples.Sum(sample => sample.Failed)));
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesEachRequestInItsOwnScope()
    {
        await using var serviceProvider = CreateServiceProvider();
        using var backgroundService = CreateBackgroundService(serviceProvider, workerCount: 2);

        await backgroundService.StartAsync(TestContext.Current.CancellationToken);
        _queue.TryEnqueue("found");
        _queue.TryEnqueue("missing");
        await WaitForProcessedSamplesAsync(2);
        await backgroundService.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, _behavior.ProcessorInstances.Distinct().Count());
    }

    private ServiceProvider CreateServiceProvider() => new ServiceCollection()
        .AddSingleton(_behavior)
        .AddScoped<TestProcessor>()
        .BuildServiceProvider();

    private QueueBackgroundService<string, TestProcessor> CreateBackgroundService(ServiceProvider serviceProvider, int workerCount) => new(
        _queue,
        workerCount,
        _monitor,
        serviceProvider.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<QueueBackgroundService<string, TestProcessor>>.Instance);

    private async Task<List<QueueSample>> WaitForProcessedSamplesAsync(int processedCount)
    {
        var samples = new List<QueueSample>();
        await WaitUntilAsync(() =>
        {
            _monitor.Sample();
            samples = [.. Assert.Single(_monitor.GetSnapshots()).Samples];
            return samples.Sum(sample => sample.Succeeded + sample.NoResult + sample.Failed) >= processedCount;
        });

        return samples;
    }

    // polls an observable condition of the asynchronous processing instead of relying on a fixed delay
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    public void Dispose() => _meterFactory.Dispose();

    private sealed class TestQueue() : BackgroundQueue<string>("Test", 10)
    {
        protected override object GetKey(string request) => request;
    }

    private sealed class TestProcessorBehavior
    {
        public ConcurrentBag<TestProcessor> ProcessorInstances { get; } = [];
    }

    // "found" produces a result, "failing" throws, anything else produces no result
    private sealed class TestProcessor(TestProcessorBehavior behavior) : IQueueProcessor<string>
    {
        public Task<bool> ProcessAsync(string request, CancellationToken cancellationToken)
        {
            behavior.ProcessorInstances.Add(this);

            return request == "failing"
                ? throw new InvalidOperationException("Processing failed.")
                : Task.FromResult(request == "found");
        }
    }
}
