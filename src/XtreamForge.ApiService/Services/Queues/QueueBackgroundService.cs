using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Services.Queues;

/// <summary>
/// Consumes a <see cref="BackgroundQueue{TRequest}"/> with <paramref name="workerCount"/> concurrent workers. Each request is
/// processed in its own DI scope by <typeparamref name="TProcessor"/>, its outcome is reported to <see cref="QueueMonitor"/>,
/// and it is completed whatever the outcome, so that a failed request can be enqueued again later.
/// </summary>
public sealed class QueueBackgroundService<TRequest, TProcessor>(
    BackgroundQueue<TRequest> queue,
    int workerCount,
    QueueMonitor queueMonitor,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<QueueBackgroundService<TRequest, TProcessor>> logger) : BackgroundService
    where TRequest : notnull
    where TProcessor : IQueueProcessor<TRequest>
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(Enumerable.Range(0, workerCount).Select(_ => ProcessQueueAsync(stoppingToken)));

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<TProcessor>();

                var hasResult = await processor.ProcessAsync(request, stoppingToken);
                queueMonitor.RecordProcessed(queue.Name, hasResult ? QueueItemOutcome.Succeeded : QueueItemOutcome.NoResult);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                queueMonitor.RecordProcessed(queue.Name, QueueItemOutcome.Failed);
                // the ToString of the requests never exposes the upstream credentials they carry
                logger.LogWarning(
                    exception,
                    "Background queue {Queue} failed to process {Request}: {ErrorMessage}",
                    queue.Name,
                    request.ToString(),
                    XtreamCredentialRedaction.SanitizeText(exception.Message));
            }
            finally
            {
                queue.Complete(request);
            }
        }
    }
}
