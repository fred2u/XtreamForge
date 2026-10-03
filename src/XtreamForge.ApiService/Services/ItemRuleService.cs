
using System.Text.Json.Nodes;

namespace XtreamForge.ApiService.Services;

public static class ItemRuleService
{
    public static Domain.Enums.InclusionDecision ApplyRules(JsonObject item, IReadOnlyList<Domain.Items.ItemRule> orderedRules)
    {
        var name = item["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(name))
            return Domain.Enums.InclusionDecision.Exclude;

        foreach (var rule in orderedRules)
        {
            var isMatch = RuleMatcher.IsMatch(name, rule.Operator, rule.Pattern, rule.CaseSensitive);
            if (isMatch)
            {
                if (rule.Action == Domain.Enums.RuleAction.Include)
                    return Domain.Enums.InclusionDecision.Include;
                if (rule.Action == Domain.Enums.RuleAction.Exclude)
                    return Domain.Enums.InclusionDecision.Exclude;
            }
        }
        return Domain.Enums.InclusionDecision.Include;
    }
}
