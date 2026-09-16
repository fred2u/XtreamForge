using XtreamForge.Categories;
using XtreamForge.Items;

namespace XtreamForge.Data;

public sealed class ItemRule
{
    public int Id { get; set; }

    public int XtreamSourceId { get; set; }

    public XtreamSource XtreamSource { get; set; } = null!;

    public ContentType ContentType { get; set; }

    public int Sequence { get; set; }

    public ItemRuleField Field { get; set; }

    public ItemRuleAction Action { get; set; }

    public ItemRuleOperator Operator { get; set; }

    public required string Pattern { get; set; }

    public bool CaseSensitive { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
