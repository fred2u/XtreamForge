using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;

namespace XtreamForge.Domain.Items;

public sealed class StreamTmdbMapping
{
    public int Id { get; set; }

    public int XtreamSourceId { get; set; }
    public XtreamSource XtreamSource { get; set; } = null!;

    public ContentType ContentType { get; set; }

    public required string StreamId { get; set; }

    /// <summary>Null while no TMDB ID has been found; the lookup is then retried from <see cref="NextLookupAtUtc"/>.</summary>
    public long? TmdbId { get; set; }

    /// <summary>Number of lookups that ended without a TMDB ID (no result or failure).</summary>
    public int LookupAttemptCount { get; set; }

    public DateTimeOffset? NextLookupAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
