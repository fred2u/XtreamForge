using XtreamForge.Domain.Enums;

namespace XtreamForge.Domain.History;

/// <summary>
/// A playback started through the proxy, identified by its TMDB ID; the same TMDB ID appears once per playback.
/// The history is global: it does not keep the Xtream account nor the source.
/// </summary>
public sealed class WatchHistoryEntry
{
    public int Id { get; set; }

    public ContentType ContentType { get; set; }

    public long TmdbId { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
}
