namespace XtreamForge.Items;

public sealed class ItemRuleEvaluator
{
    public ItemRuleEvaluationResult Evaluate(ItemRuleInput input, IReadOnlyList<ItemRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules.OrderBy(rule => rule.Sequence).ThenBy(rule => rule.Id ?? int.MaxValue))
        {
            if (!rule.IsEnabled)
            {
                continue;
            }

            if (!Matches(input, rule))
            {
                continue;
            }

            return new ItemRuleEvaluationResult(
                rule.Action == ItemRuleAction.Include ? ItemInclusionDecision.Include : ItemInclusionDecision.Exclude,
                rule.Id,
                rule.Sequence,
                rule.Field,
                rule.Action,
                rule.Operator,
                rule.Pattern,
                rule.CaseSensitive);
        }

        return new ItemRuleEvaluationResult(ItemInclusionDecision.Include, null, null, null, null, null, null, null);
    }

    public static bool Matches(ItemRuleInput input, ItemRuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rule);

        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            return false;
        }

        var value = GetFieldValue(input, rule.Field);
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var comparison = rule.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return rule.Operator switch
        {
            ItemRuleOperator.StartsWith => value.StartsWith(rule.Pattern, comparison),
            ItemRuleOperator.Contains => value.Contains(rule.Pattern, comparison),
            _ => false
        };
    }

    private static string? GetFieldValue(ItemRuleInput input, ItemRuleField field) => field switch
    {
        ItemRuleField.Name => input.Name,
        _ => null
    };
}

public sealed record ItemRuleInput(string? Name);

public sealed record ItemRuleDefinition(
    int? Id,
    int Sequence,
    ItemRuleField Field,
    ItemRuleAction Action,
    ItemRuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record ItemRuleEvaluationResult(
    ItemInclusionDecision Decision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    ItemRuleField? MatchedRuleField,
    ItemRuleAction? MatchedRuleAction,
    ItemRuleOperator? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive)
{
    public bool IsMatch => MatchedRuleId is not null;
}
