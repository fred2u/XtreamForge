using XtreamForge.Domain.Enums;

namespace XtreamForge.Domain.Tmdb;

/// <summary>
/// Rule evaluated on the TMDB metadata of the returned items, after the enrichment. Unlike the item rules, TMDB rules are global
/// per content type, because the TMDB metadata does not depend on the source.
/// </summary>
public sealed class TmdbRule
{
    public int Id { get; set; }

    public ContentType ContentType { get; set; }

    public int Sequence { get; set; }

    public TmdbRuleField Field { get; set; }

    public RuleAction Action { get; set; }

    public RuleOperator Operator { get; set; }

    public required string Pattern { get; set; }

    public bool CaseSensitive { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
