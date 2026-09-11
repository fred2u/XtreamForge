using XtreamForge.Infrastructure.Models;

namespace XtreamForge.Infrastructure.Data;

public sealed class CategoryRule
{
    public int Id { get; set; }

    public int XtreamSourceId { get; set; }

    public XtreamSource XtreamSource { get; set; } = null!;

    public ContentType ContentType { get; set; }

    public int Sequence { get; set; }

    public CategoryRuleAction Action { get; set; }

    public CategoryRuleOperator Operator { get; set; }

    public required string Pattern { get; set; }

    public bool CaseSensitive { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
