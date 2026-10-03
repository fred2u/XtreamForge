namespace XtreamForge.ApiService.Services.Monitoring;

/// <summary>Samples the background queues every <see cref="QueueMonitor.SampleInterval"/>.</summary>
public sealed class QueueMonitorSamplingService(QueueMonitor monitor, TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(QueueMonitor.SampleInterval, timeProvider);

        monitor.Sample();
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            monitor.Sample();
        }
    }
}
