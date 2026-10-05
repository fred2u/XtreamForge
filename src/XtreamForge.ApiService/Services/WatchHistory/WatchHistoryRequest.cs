using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Services.WatchHistory;

/// <summary>
/// Playback to record in the watch history. The upstream credentials of <see cref="Movie"/> are kept in memory only
/// and are not exposed by its <c>ToString</c>.
/// </summary>
public sealed record WatchHistoryRequest(string Protocol, string Host, int Port, XtreamMovieStream Movie, DateTimeOffset StartedAtUtc);
