using XtreamForge.ApiService.Services.Queues;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.TmdbInfos;

/// <summary>Queue of the TMDB metadata loads, deduplicated per content type and TMDB ID.</summary>
public sealed class TmdbInfoQueue() : BackgroundQueue<TmdbInfoRequest>(QueueName, Capacity)
{
    public const string QueueName = "TMDB info loads";

    private const int Capacity = 50000;

    // the request only holds the content type and the TMDB ID
    protected override object GetKey(TmdbInfoRequest request) => request;

    protected override bool Accepts(TmdbInfoRequest request)
        => request.Type is (ContentType.Vod or ContentType.Series) && request.TmdbId > 0;
}
