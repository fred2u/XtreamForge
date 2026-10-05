using System.Text.Json.Nodes;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Rules;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Services;

/// <summary>
/// Evaluation of the category, item, and TMDB rules. The enabled rules are evaluated by ascending sequence and the first matching
/// rule decides: an <c>Exclude</c> rule excludes, an <c>Include</c> rule includes; when no rule matches, the subject is included.
/// </summary>
public static class RuleEvaluator
{
    /// <summary>Keeps the enabled rules, in evaluation order.</summary>
    public static IReadOnlyList<TRule> OrderEnabled<TRule>(IEnumerable<TRule> rules) where TRule : IRule
        => [.. rules.Where(rule => rule.IsEnabled).OrderBy(rule => rule.Sequence)];

    // ─── Category rules: matched on the category name ──────────────────────

    /// <summary>Keeps the categories that <see cref="EvaluateCategories"/> includes.</summary>
    public static IEnumerable<XtreamCategory> ApplyCategoryRules(IEnumerable<XtreamCategory> categories, IEnumerable<CategoryRule> rules)
        => EvaluateCategories(categories, rules)
            .Where(evaluation => evaluation.Decision == InclusionDecision.Include)
            .Select(evaluation => evaluation.Category);

    /// <summary>
    /// Evaluates every category: a manual exclusion wins, then a category disabled by the provider, then the category rules.
    /// </summary>
    public static IEnumerable<CategoryRuleEvaluation> EvaluateCategories(IEnumerable<XtreamCategory> categories, IEnumerable<CategoryRule> rules)
    {
        var orderedRules = OrderEnabled(rules);
        foreach (var category in categories)
        {
            yield return EvaluateCategory(category, orderedRules);
        }
    }

    private static CategoryRuleEvaluation EvaluateCategory(XtreamCategory category, IReadOnlyList<CategoryRule> orderedEnabledRules)
    {
        if (category.IsExcluded)
            return new CategoryRuleEvaluation(category, InclusionDecision.Exclude, CategoryExclusionReason.ManuallyExcluded, null);

        if (!category.IsEnabled)
            return new CategoryRuleEvaluation(category, InclusionDecision.Exclude, CategoryExclusionReason.ProviderDisabled, null);

        var decidingRule = FindDecidingRule(orderedEnabledRules, category.Name);

        return Decide(decidingRule) == InclusionDecision.Exclude
            ? new CategoryRuleEvaluation(category, InclusionDecision.Exclude, CategoryExclusionReason.Rule, decidingRule)
            : new CategoryRuleEvaluation(category, InclusionDecision.Include, null, decidingRule);
    }

    // ─── Item rules: matched on the item name sent by the provider ─────────

    /// <summary>
    /// Evaluates the item rules, already filtered and ordered by <see cref="OrderEnabled"/>, on the <c>name</c> of a provider item;
    /// an item without name is excluded.
    /// </summary>
    public static InclusionDecision EvaluateItem(JsonObject item, IReadOnlyList<ItemRule> orderedEnabledRules)
    {
        var name = item["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(name))
            return InclusionDecision.Exclude;

        return Decide(FindDecidingRule(orderedEnabledRules, name));
    }

    // ─── TMDB rules: matched on the TMDB title or genres ───────────────────

    public static IEnumerable<TmdbRuleEvaluation> EvaluateTmdbInfos(IEnumerable<TmdbInfo> infos, IEnumerable<TmdbRule> rules)
    {
        var orderedRules = OrderEnabled(rules);
        foreach (var info in infos)
        {
            yield return EvaluateTmdbInfo(info, orderedRules);
        }
    }

    /// <summary>
    /// Evaluates one TMDB metadata entry with rules already filtered and ordered by <see cref="OrderEnabled"/>:
    /// a manual exclusion wins, then the TMDB rules.
    /// </summary>
    public static TmdbRuleEvaluation EvaluateTmdbInfo(TmdbInfo info, IReadOnlyList<TmdbRule> orderedEnabledRules)
    {
        if (info.IsExcluded)
            return new TmdbRuleEvaluation(info, InclusionDecision.Exclude, TmdbExclusionReason.ManuallyExcluded, null);

        var decidingRule = orderedEnabledRules.FirstOrDefault(rule => IsMatch(info, rule));

        return Decide(decidingRule) == InclusionDecision.Exclude
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
            return IsMatch(info.Title ?? string.Empty, rule.Operator, rule.Pattern, rule.CaseSensitive);

        return rule.Operator switch
        {
            RuleOperator.NotContains => !info.Genres.Any(genre => IsMatch(genre, RuleOperator.Contains, rule.Pattern, rule.CaseSensitive)),
            RuleOperator.NotStartsWith => !info.Genres.Any(genre => IsMatch(genre, RuleOperator.StartsWith, rule.Pattern, rule.CaseSensitive)),
            _ => info.Genres.Any(genre => IsMatch(genre, rule.Operator, rule.Pattern, rule.CaseSensitive))
        };
    }

    // ─── Shared matching ───────────────────────────────────────────────────

    private static TRule? FindDecidingRule<TRule>(IReadOnlyList<TRule> orderedEnabledRules, string text) where TRule : class, IRule
        => orderedEnabledRules.FirstOrDefault(rule => IsMatch(text, rule.Operator, rule.Pattern, rule.CaseSensitive));

    private static InclusionDecision Decide(IRule? decidingRule)
        => decidingRule?.Action == RuleAction.Exclude ? InclusionDecision.Exclude : InclusionDecision.Include;

    private static bool IsMatch(string text, RuleOperator @operator, string pattern, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return @operator switch
        {
            RuleOperator.StartsWith => text.StartsWith(pattern, comparison),
            RuleOperator.Contains => text.Contains(pattern, comparison),
            RuleOperator.NotContains => !text.Contains(pattern, comparison),
            RuleOperator.NotStartsWith => !text.StartsWith(pattern, comparison),
            _ => throw new NotSupportedException($"Unsupported operator: {@operator}")
        };
    }
}
