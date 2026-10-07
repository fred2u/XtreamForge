using XtreamForge.ApiService.Services.Queues;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Services.WatchHistory;

/// <summary>Queue of the episodes listed by <c>get_series_info</c> to persist, one request per listed series.</summary>
public sealed class SeriesEpisodeQueue() : BackgroundQueue<SeriesEpisodeRequest>(QueueName, Capacity)
{
    public const string QueueName = "Series episodes";

    private const int Capacity = 1000;

    // a series listed again while its episodes are pending is not enqueued again: its episodes rarely change
    protected override object GetKey(SeriesEpisodeRequest request) => (request.XtreamSourceId, request.SeriesId);

    // the series ID comes from the query string of the client: like the episode IDs, it must have the shape of a stream ID
    protected override bool Accepts(SeriesEpisodeRequest request)
        => XtreamStreamPath.IsStreamId(request.SeriesId) && request.Episodes.Count > 0;
}
