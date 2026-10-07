using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Services.WatchHistory;

/// <summary>
/// Playback of a movie or series episode to record in the watch history. The upstream credentials of <see cref="Stream"/> are kept in memory only
/// and are not exposed by its <c>ToString</c>.
/// </summary>
public sealed record WatchHistoryRequest(string Protocol, string Host, int Port, XtreamVideoStream Stream, DateTimeOffset StartedAtUtc);
