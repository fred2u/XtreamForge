using XtreamForge.Infrastructure.Models;

namespace XtreamForge.Infrastructure.Data;

public sealed class OutputCategory
{
    public int Id { get; set; }

    public int XtreamSourceId { get; set; }

    public XtreamSource XtreamSource { get; set; } = null!;

    public ContentType ContentType { get; set; }

    public int XtreamForgeCategoryId { get; set; }

    public required string DisplayName { get; set; }

    public int SortOrder { get; set; }

    public bool IsNameCustomized { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<UpstreamCategory> MappedUpstreamCategories { get; set; } = [];

    public ICollection<UpstreamCategory> DedicatedUpstreamCategories { get; set; } = [];
}
