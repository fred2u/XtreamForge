using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Services;

/// <summary>
/// Evaluates the manual exclusion and the TMDB rules on the TMDB metadata: a manual exclusion wins, then the first matching
/// enabled rule by ascending sequence decides; an entry no rule matches is included.
/// </summary>
public static class TmdbRuleService
{
    /// <summary>Keeps the enabled rules, in evaluation order.</summary>
    public static IReadOnlyList<TmdbRule> OrderEnabled(IEnumerable<TmdbRule> rules)
        => [.. rules.Where(rule => rule.IsEnabled).OrderBy(rule => rule.Sequence)];

    public static IEnumerable<TmdbRuleEvaluation> Evaluate(IEnumerable<TmdbInfo> infos, IEnumerable<TmdbRule> rules)
    {
        var orderedRules = OrderEnabled(rules);
        foreach (var info in infos)
        {
            yield return Evaluate(info, orderedRules);
        }
    }

    /// <summary>Evaluates one entry with rules already filtered and ordered by <see cref="OrderEnabled"/>.</summary>
    public static TmdbRuleEvaluation Evaluate(TmdbInfo info, IReadOnlyList<TmdbRule> orderedEnabledRules)
    {
        if (info.IsExcluded)
            return new TmdbRuleEvaluation(info, InclusionDecision.Exclude, TmdbExclusionReason.ManuallyExcluded, null);

        var decidingRule = orderedEnabledRules.FirstOrDefault(rule => IsMatch(info, rule));

        return decidingRule?.Action == RuleAction.Exclude
            ? new TmdbRuleEvaluation(info, InclusionDecision.Exclude, TmdbExclusionReason.Rule, decidingRule)
            : new TmdbRuleEvaluation(info, InclusionDecision.Include, null, decidingRule);
    }

    /// <summary>
    /// A title rule matches the TMDB title (an entry without title has an empty title). A genre rule is evaluated per genre:
    /// "starts with" and "contains" match when at least one genre matches, "does not start with" and "does not contain" when no genre does.
    /// </summary>
    public static bool IsMatch(TmdbInfo info, TmdbRule rule)
    {
        if (rule.Field == TmdbRuleField.Title)
            return RuleMatcher.IsMatch(info.Title ?? string.Empty, rule.Operator, rule.Pattern, rule.CaseSensitive);

        return rule.Operator switch
        {
            RuleOperator.NotContains => !info.Genres.Any(genre => RuleMatcher.IsMatch(genre, RuleOperator.Contains, rule.Pattern, rule.CaseSensitive)),
            RuleOperator.NotStartsWith => !info.Genres.Any(genre => RuleMatcher.IsMatch(genre, RuleOperator.StartsWith, rule.Pattern, rule.CaseSensitive)),
            _ => info.Genres.Any(genre => RuleMatcher.IsMatch(genre, rule.Operator, rule.Pattern, rule.CaseSensitive))
        };
    }
}
