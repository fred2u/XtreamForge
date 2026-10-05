using XtreamForge.ApiService.Services.Queues;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services;

/// <summary>Queue of the TMDB ID lookups, deduplicated per source, content type, and stream.</summary>
public sealed class TmdbIdRetrieverQueue() : BackgroundQueue<TmdbIdRetrieverRequest>(QueueName, Capacity)
{
    public const string QueueName = "TMDB ID lookups";

    private const int Capacity = 50000;

    protected override object GetKey(TmdbIdRetrieverRequest request)
        => (request.XtreamSourceId, request.Type, request.StreamId);

    protected override bool Accepts(TmdbIdRetrieverRequest request)
        => request.Type is ContentType.Vod or ContentType.Series;
}
