using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;

namespace XtreamForge.Domain.History;

/// <summary>
/// A playback started through the proxy, identified by its TMDB ID; the same TMDB ID appears once per playback.
/// A series episode is identified by the TMDB ID of its series, with its season and episode numbers when the provider gives them.
/// The history is global: it does not keep the Xtream account, and its source is informative only.
/// </summary>
public sealed class WatchHistoryEntry
{
    public int Id { get; set; }

    /// <summary>Source that served the playback; null for a playback added by the admin, or once its source is deleted.</summary>
    public int? XtreamSourceId { get; set; }
    public XtreamSource? XtreamSource { get; set; }

    public ContentType ContentType { get; set; }

    public long TmdbId { get; set; }

    /// <summary>Season of a series episode; null for a movie, or when unknown.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Number of a series episode in its season; null for a movie, or when unknown.</summary>
    public int? EpisodeNumber { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
}
