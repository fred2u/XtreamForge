using XtreamForge.Categories;

namespace XtreamForge.Categories;

public sealed class CategoryRuleEvaluator
{
    public CategoryRuleEvaluationResult Evaluate(string categoryName, IReadOnlyList<CategoryRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return EvaluateOrdered(
            categoryName,
            rules
                .OrderBy(rule => rule.Sequence)
                .ThenBy(rule => rule.Id ?? int.MaxValue)
                .ToArray());
    }

    public CategoryRuleEvaluationResult EvaluateOrdered(string categoryName, IReadOnlyList<CategoryRuleDefinition> orderedRules)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        ArgumentNullException.ThrowIfNull(orderedRules);

        foreach (var rule in orderedRules)
        {
            if (!rule.IsEnabled)
            {
                continue;
            }

            if (!Matches(categoryName, rule))
            {
                continue;
            }

            return new CategoryRuleEvaluationResult(
                rule.Action == CategoryRuleAction.Include ? CategoryInclusionDecision.Include : CategoryInclusionDecision.Exclude,
                rule.Id,
                rule.Sequence,
                rule.Action,
                rule.Operator,
                rule.Pattern,
                rule.CaseSensitive);
        }

        return new CategoryRuleEvaluationResult(CategoryInclusionDecision.Include, null, null, null, null, null, null);
    }

    public static bool Matches(string categoryName, CategoryRuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        ArgumentNullException.ThrowIfNull(rule);

        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            return false;
        }

        var comparison = rule.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return rule.Operator switch
        {
            CategoryRuleOperator.StartsWith => categoryName.StartsWith(rule.Pattern, comparison),
            CategoryRuleOperator.Contains => categoryName.Contains(rule.Pattern, comparison),
            _ => false
        };
    }
}

public sealed record CategoryRuleDefinition(
    int? Id,
    int Sequence,
    CategoryRuleAction Action,
    CategoryRuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record CategoryRuleEvaluationResult(
    CategoryInclusionDecision Decision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    CategoryRuleAction? MatchedRuleAction,
    CategoryRuleOperator? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive)
{
    public bool IsMatch => MatchedRuleId is not null;
}
