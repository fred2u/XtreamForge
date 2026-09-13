using XtreamForge.Infrastructure.Models;

namespace XtreamForge.Infrastructure.Data;

public sealed class CustomCategory
{
    public int Id { get; set; }

    public ContentType ContentType { get; set; }

    public int XtreamForgeCategoryId { get; set; }

    public required string DisplayName { get; set; }

    public string? NormalizedDisplayName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<UpstreamCategory> UpstreamCategories { get; set; } = [];
}
