using XtreamForge.ApiService.Services.Queues;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.TmdbIdRetriever;

/// <summary>Queue of the provider TMDB IDs of list items to persist, one request per batch of items.</summary>
public sealed class ProviderTmdbIdQueue() : BackgroundQueue<ProviderTmdbIdRequest>(QueueName, Capacity)
{
    public const string QueueName = "Provider TMDB IDs";

    private const int Capacity = 1000;

    // batches are not deduplicated: the dictionary is compared by reference, and persisting a batch again changes nothing
    protected override object GetKey(ProviderTmdbIdRequest request) => request;

    protected override bool Accepts(ProviderTmdbIdRequest request)
        => request.Type is (ContentType.Vod or ContentType.Series) && request.TmdbIds.Count > 0;
}
