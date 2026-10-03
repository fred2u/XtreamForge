using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Services;

public sealed class TmdbIdRetrieverBackgroundService(TmdbIdRetrieverQueue queue, QueueMonitor queueMonitor, IServiceScopeFactory serviceScopeFactory, ILogger<TmdbIdRetrieverBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const int workerCount = 2;

        var workers = Enumerable
            .Range(0, workerCount)
            .Select(_ => ProcessQueueAsync(stoppingToken));

        await Task.WhenAll(workers);
    }

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var tmdbIdRetrieverService = scope.ServiceProvider.GetRequiredService<TmdbIdRetrieverService>();

                var found = await tmdbIdRetrieverService.RetrieveAsync(request, stoppingToken);
                queueMonitor.RecordProcessed(queue.Name, found ? QueueItemOutcome.Succeeded : QueueItemOutcome.NoResult);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                queueMonitor.RecordProcessed(queue.Name, QueueItemOutcome.Failed);
                logger.LogWarning(ex, "TMDB ID retriever service failed for {StreamId} ({Type}): {ErrorMessage}", request.StreamId, request.Type, XtreamCredentialRedaction.SanitizeText(ex.Message));
            }
            finally
            {
                queue.Complete(request);
            }
        }
    }
}
