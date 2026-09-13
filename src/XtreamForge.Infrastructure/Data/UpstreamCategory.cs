using XtreamForge.Infrastructure.Models;

namespace XtreamForge.Infrastructure.Data;

public sealed class UpstreamCategory
{
    public int Id { get; set; }

    public int XtreamSourceId { get; set; }

    public XtreamSource XtreamSource { get; set; } = null!;

    public ContentType ContentType { get; set; }

    public required string UpstreamCategoryId { get; set; }

    public required string UpstreamCategoryName { get; set; }

    public int DedicatedOutputCategoryId { get; set; }

    public OutputCategory DedicatedOutputCategory { get; set; } = null!;

    public int? CustomCategoryId { get; set; }

    public CustomCategory? CustomCategory { get; set; }

    public bool IsExcluded { get; set; }

    public DateTimeOffset FirstDiscoveredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastDiscoveredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
