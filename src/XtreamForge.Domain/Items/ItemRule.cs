using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Rules;
using XtreamForge.Domain.Sources;

namespace XtreamForge.Domain.Items;

public sealed class ItemRule : ISourceRule
{
    public int Id { get; set; }

    public int XtreamSourceId { get; set; }
    public XtreamSource XtreamSource { get; set; } = null!;

    public ContentType ContentType { get; set; }

    public int Sequence { get; set; }

    public RuleAction Action { get; set; }

    public RuleOperator Operator { get; set; }

    public string Pattern { get; set; } = string.Empty;

    public bool CaseSensitive { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
