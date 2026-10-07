namespace XtreamForge.ApiService.Services.WatchHistory;

/// <summary>Episode listed by <c>get_series_info</c>: its ID and, when the provider gives them, its season and number.</summary>
public sealed record XtreamEpisode(string EpisodeId, int? SeasonNumber, int? EpisodeNumber);

/// <summary>
/// Episodes of a series listed by the provider <c>get_series_info</c>, to persist in the background so that the playback of an episode,
/// whose stream URL only carries the episode ID, can be recorded with its series.
/// </summary>
public sealed record SeriesEpisodeRequest(int XtreamSourceId, string SeriesId, IReadOnlyList<XtreamEpisode> Episodes)
{
    // the compiler-generated ToString would only print the type of the list
    public override string ToString() => $"{nameof(SeriesEpisodeRequest)} {{ XtreamSourceId = {XtreamSourceId}, SeriesId = {SeriesId}, Count = {Episodes.Count} }}";
}
