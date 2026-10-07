using XtreamForge.Domain.Sources;

namespace XtreamForge.Domain.Items;

/// <summary>
/// Episode of a series as listed by the provider <c>get_series_info</c>: a stream URL of an episode only carries its ID,
/// which identifies its series, season, and number once the series has been listed.
/// </summary>
public sealed class SeriesEpisode
{
    public int Id { get; set; }

    public int XtreamSourceId { get; set; }
    public XtreamSource XtreamSource { get; set; } = null!;

    public required string EpisodeId { get; set; }

    /// <summary>Stream ID of the series (<c>series_id</c>), the key of its <see cref="StreamTmdbMapping"/>.</summary>
    public required string SeriesId { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
