using XtreamForge.Domain.Enums;

namespace XtreamForge.Domain.Tmdb;

/// <summary>
/// Outcome of the manual exclusion and the TMDB rules for one TMDB metadata entry.
/// <paramref name="DecidingRule"/> is the first matching enabled rule, or null when the entry is excluded manually or no rule matched.
/// </summary>
public sealed record TmdbRuleEvaluation(
    TmdbInfo Info,
    InclusionDecision Decision,
    TmdbExclusionReason? ExclusionReason,
    TmdbRule? DecidingRule);
