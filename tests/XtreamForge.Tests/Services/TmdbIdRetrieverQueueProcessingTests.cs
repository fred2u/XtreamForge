using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.ApiService.Services.Queues;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services;

/// <summary>The TMDB ID lookups of <see cref="TmdbIdRetrieverQueue"/>, processed by <see cref="TmdbIdRetrieverService"/> in the background.</summary>
public class TmdbIdRetrieverQueueProcessingTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly XtreamForgeDbContext _dbContext;
    private readonly TestMeterFactory _meterFactory = new();
    private readonly TmdbIdRetrieverQueue _queue = new();
    private readonly QueueMonitor _monitor;

    public TmdbIdRetrieverQueueProcessingTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _monitor = new QueueMonitor([_queue], TimeProvider.System, _meterFactory);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesQueuedRequestAndCompletesIt()
    {
        var source = await AddSourceAsync();
        var providerFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_info"] = (HttpStatusCode.OK, """{ "info": { "tmdb_id": "603" } }""")
        });
        await using var serviceProvider = CreateServiceProvider(providerFactory);
        using var backgroundService = CreateBackgroundService(serviceProvider);
        var request = CreateRequest(source.Id);

        await backgroundService.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(_queue.TryEnqueue(request));

        // the request is completed once it can be enqueued again
        await WaitUntilAsync(() => _queue.TryEnqueue(request));
        await backgroundService.StopAsync(TestContext.Current.CancellationToken);

        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(603, mapping.TmdbId);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{ "info": { "tmdb_id": "603" } }""", QueueItemOutcome.Succeeded)]
    [InlineData(HttpStatusCode.OK, """{ "info": {} }""", QueueItemOutcome.NoResult)]
    [InlineData(HttpStatusCode.Unauthorized, "", QueueItemOutcome.Failed)]
    public async Task ExecuteAsync_ReportsTheOutcomeToTheQueueMonitor(HttpStatusCode statusCode, string content, QueueItemOutcome expectedOutcome)
    {
        var source = await AddSourceAsync();
        var providerFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_info"] = (statusCode, content)
        });
        await using var serviceProvider = CreateServiceProvider(providerFactory);
        using var backgroundService = CreateBackgroundService(serviceProvider);
        var samples = new List<QueueSample>();

        await backgroundService.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(_queue.TryEnqueue(CreateRequest(source.Id)));

        await WaitUntilAsync(() =>
        {
            _monitor.Sample();
            samples = [.. Assert.Single(_monitor.GetSnapshots()).Samples];
            return samples.Sum(sample => sample.Succeeded + sample.NoResult + sample.Failed) > 0;
        });
        await backgroundService.StopAsync(TestContext.Current.CancellationToken);

        var processed = (
            Succeeded: samples.Sum(sample => sample.Succeeded),
            NoResult: samples.Sum(sample => sample.NoResult),
            Failed: samples.Sum(sample => sample.Failed));
        Assert.Equal(
            (expectedOutcome == QueueItemOutcome.Succeeded ? 1 : 0, expectedOutcome == QueueItemOutcome.NoResult ? 1 : 0, expectedOutcome == QueueItemOutcome.Failed ? 1 : 0),
            processed);
    }

    private async Task<XtreamSource> AddSourceAsync()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return source;
    }

    private ServiceProvider CreateServiceProvider(IHttpClientFactory providerFactory) => new ServiceCollection()
        .AddLogging()
        .AddSingleton(_dbContext)
        .AddSingleton(providerFactory)
        .AddSingleton(TimeProvider.System)
        .AddSingleton(new StubTmdbHttpClientFactory(_ => null).CreateMatcher(apiKey: ""))
        .AddSingleton(new TmdbInfoQueue())
        .AddScoped<TmdbIdRetrieverService>()
        .BuildServiceProvider();

    private QueueBackgroundService<TmdbIdRetrieverRequest, TmdbIdRetrieverService> CreateBackgroundService(ServiceProvider serviceProvider) => new(
        _queue,
        workerCount: 2,
        _monitor,
        serviceProvider.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<QueueBackgroundService<TmdbIdRetrieverRequest, TmdbIdRetrieverService>>.Instance);

    private static TmdbIdRetrieverRequest CreateRequest(int sourceId) =>
        new(sourceId, "http", "provider.example.com", 8080, "user", "secret", "42", ContentType.Vod);

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
