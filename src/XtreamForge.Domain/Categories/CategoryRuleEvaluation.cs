using XtreamForge.Domain.Enums;

namespace XtreamForge.Domain.Categories;

/// <summary>
/// Outcome of the category rules for one category.
/// <paramref name="DecidingRule"/> is the first matching enabled rule, or null when no rule was reached or none matched.
/// </summary>
public sealed record CategoryRuleEvaluation(
    XtreamCategory Category,
    InclusionDecision Decision,
    CategoryExclusionReason? ExclusionReason,
    CategoryRule? DecidingRule);
