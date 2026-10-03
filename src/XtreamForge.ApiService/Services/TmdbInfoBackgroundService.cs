using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Services;

public sealed class TmdbInfoBackgroundService(TmdbInfoQueue queue, QueueMonitor queueMonitor, IServiceScopeFactory serviceScopeFactory, ILogger<TmdbInfoBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var tmdbInfoService = scope.ServiceProvider.GetRequiredService<TmdbInfoService>();

                var loaded = await tmdbInfoService.LoadAsync(request, stoppingToken);
                queueMonitor.RecordProcessed(queue.Name, loaded ? QueueItemOutcome.Succeeded : QueueItemOutcome.NoResult);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                queueMonitor.RecordProcessed(queue.Name, QueueItemOutcome.Failed);
                logger.LogWarning(ex, "TMDB info load failed for {TmdbId} ({Type}): {ErrorMessage}", request.TmdbId, request.Type, XtreamCredentialRedaction.SanitizeText(ex.Message));
            }
            finally
            {
                queue.Complete(request);
            }
        }
    }
}
