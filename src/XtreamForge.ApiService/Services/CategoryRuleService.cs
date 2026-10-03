using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services;

public static class CategoryRuleService
{
    public static IEnumerable<XtreamCategory> ApplyRules(IEnumerable<XtreamCategory> xtreamCategories, IEnumerable<CategoryRule> rules)
    {
        return Evaluate(xtreamCategories, rules)
            .Where(evaluation => evaluation.Decision == InclusionDecision.Include)
            .Select(evaluation => evaluation.Category);
    }

    /// <summary>
    /// Evaluates every category: a manual exclusion wins, then a category disabled by the provider,
    /// then the first matching enabled rule by ascending sequence; a category no rule matches is included.
    /// </summary>
    public static IEnumerable<CategoryRuleEvaluation> Evaluate(IEnumerable<XtreamCategory> xtreamCategories, IEnumerable<CategoryRule> rules)
    {
        var orderedRules = rules.Where(r => r.IsEnabled).OrderBy(r => r.Sequence).ToList();
        foreach (var xtreamCategory in xtreamCategories)
        {
            yield return Evaluate(xtreamCategory, orderedRules);
        }
    }

    private static CategoryRuleEvaluation Evaluate(XtreamCategory xtreamCategory, List<CategoryRule> orderedRules)
    {
        if (xtreamCategory.IsExcluded)
            return new CategoryRuleEvaluation(xtreamCategory, InclusionDecision.Exclude, CategoryExclusionReason.ManuallyExcluded, null);

        if (!xtreamCategory.IsEnabled)
            return new CategoryRuleEvaluation(xtreamCategory, InclusionDecision.Exclude, CategoryExclusionReason.ProviderDisabled, null);

        foreach (var rule in orderedRules)
        {
            var isMatch = RuleMatcher.IsMatch(xtreamCategory.Name, rule.Operator, rule.Pattern, rule.CaseSensitive);
            if (isMatch)
            {
                if (rule.Action == RuleAction.Include)
                    return new CategoryRuleEvaluation(xtreamCategory, InclusionDecision.Include, null, rule);
                if (rule.Action == RuleAction.Exclude)
                    return new CategoryRuleEvaluation(xtreamCategory, InclusionDecision.Exclude, CategoryExclusionReason.Rule, rule);
            }
        }
        return new CategoryRuleEvaluation(xtreamCategory, InclusionDecision.Include, null, null);
    }
}
