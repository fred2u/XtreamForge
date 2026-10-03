using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Services;

public sealed class WatchHistoryBackgroundService(WatchHistoryQueue queue, QueueMonitor queueMonitor, IServiceScopeFactory serviceScopeFactory, ILogger<WatchHistoryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var watchHistoryService = scope.ServiceProvider.GetRequiredService<WatchHistoryService>();

                var recorded = await watchHistoryService.RecordAsync(request, stoppingToken);
                queueMonitor.RecordProcessed(queue.Name, recorded ? QueueItemOutcome.Succeeded : QueueItemOutcome.NoResult);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                queueMonitor.RecordProcessed(queue.Name, QueueItemOutcome.Failed);
                logger.LogWarning(ex, "Watch history record failed for movie {StreamId}: {ErrorMessage}", request.Movie.StreamId, XtreamCredentialRedaction.SanitizeText(ex.Message));
            }
        }
    }
}
