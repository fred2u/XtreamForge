using XtreamForge.Domain.Enums;

namespace XtreamForge.Domain.Rules;

/// <summary>
/// A filtering rule set by the administrator (category, item, or TMDB rule). The enabled rules of a scope are evaluated
/// by ascending <see cref="Sequence"/>, unique in the scope, and the first matching rule decides.
/// </summary>
public interface IRule
{
    int Id { get; }

    ContentType ContentType { get; set; }

    int Sequence { get; set; }

    RuleAction Action { get; set; }

    RuleOperator Operator { get; set; }

    string Pattern { get; set; }

    bool CaseSensitive { get; set; }

    bool IsEnabled { get; set; }

    DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>A rule defined per source (category and item rules): its scope is a content type of a source.</summary>
public interface ISourceRule : IRule
{
    int XtreamSourceId { get; set; }
}
