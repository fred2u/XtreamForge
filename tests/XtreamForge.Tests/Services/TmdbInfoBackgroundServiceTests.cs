using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services;

public class TmdbInfoBackgroundServiceTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly XtreamForgeDbContext _dbContext;
    private readonly TestMeterFactory _meterFactory = new();
    private readonly TmdbInfoQueue _queue = new();
    private readonly QueueMonitor _monitor;

    public TmdbInfoBackgroundServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _monitor = new QueueMonitor([_queue], TimeProvider.System, _meterFactory);
    }

    [Theory]
    [InlineData("""{ "id": 603, "title": "Matrix" }""", QueueItemOutcome.Succeeded)]
    [InlineData(null, QueueItemOutcome.NoResult)]
    [InlineData("not json", QueueItemOutcome.Failed)]
    public async Task ExecuteAsync_LoadsQueuedRequestAndReportsTheOutcome(string? tmdbResponse, QueueItemOutcome expectedOutcome)
    {
        var tmdbInfoService = new StubTmdbHttpClientFactory(_ => tmdbResponse).CreateTmdbInfoService(_dbContext, TimeProvider.System);
        await using var serviceProvider = new ServiceCollection().AddSingleton(tmdbInfoService).BuildServiceProvider();
        using var backgroundService = new TmdbInfoBackgroundService(_queue, _monitor, serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TmdbInfoBackgroundService>.Instance);
        var request = new TmdbInfoRequest(ContentType.Vod, 603);
        var samples = new List<QueueSample>();

        await backgroundService.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(_queue.TryEnqueue(request));

        await WaitUntilAsync(() =>
        {
            _monitor.Sample();
            samples = [.. Assert.Single(_monitor.GetSnapshots()).Samples];
            return samples.Sum(sample => sample.Succeeded + sample.NoResult + sample.Failed) > 0;
        });
        await backgroundService.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            (expectedOutcome == QueueItemOutcome.Succeeded ? 1 : 0, expectedOutcome == QueueItemOutcome.NoResult ? 1 : 0, expectedOutcome == QueueItemOutcome.Failed ? 1 : 0),
            (samples.Sum(sample => sample.Succeeded), samples.Sum(sample => sample.NoResult), samples.Sum(sample => sample.Failed)));
        var info = await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedOutcome == QueueItemOutcome.Succeeded, info.LoadedAtUtc is not null);
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

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _meterFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}
