using XtreamForge.Categories;

namespace XtreamForge.Data;

public sealed class StreamTmdbMapping
{
    public int Id { get; set; }

    public int XtreamSourceId { get; set; }

    public XtreamSource XtreamSource { get; set; } = null!;

    public ContentType ContentType { get; set; }

    public required string StreamId { get; set; }

    public long TmdbId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
