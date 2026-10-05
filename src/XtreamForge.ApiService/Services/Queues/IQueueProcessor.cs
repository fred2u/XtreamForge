namespace XtreamForge.ApiService.Services.Queues;

/// <summary>
/// Processes the requests of a <see cref="BackgroundQueue{TRequest}"/>; resolved in a new DI scope for each request by
/// <see cref="QueueBackgroundService{TRequest, TProcessor}"/>.
/// </summary>
public interface IQueueProcessor<in TRequest>
{
    /// <summary>Returns true when the request produced a result, false when there was nothing to do or nothing was found.</summary>
    Task<bool> ProcessAsync(TRequest request, CancellationToken cancellationToken);
}
