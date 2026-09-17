using XtreamForge.Source;

namespace XtreamForge.Categories;

public sealed class XtreamCategoryMappingService(CategoryRuleEvaluator ruleEvaluator)
{
    public IReadOnlyList<RewrittenCategory> BuildRewrittenCategories(
        IReadOnlyList<SourceCategorySnapshot> upstreamCategories,
        IReadOnlyList<CategoryRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(upstreamCategories);
        ArgumentNullException.ThrowIfNull(rules);

        return [.. BuildEffectiveOutputCategoryMappings(upstreamCategories, rules)
            .Select(category => new RewrittenCategory(
                category.XtreamForgeCategoryId.ToString(),
                category.DisplayName,
                category.IncludedUpstreamCategoryIds))];
    }

    public IReadOnlyList<EffectiveOutputCategoryMapping> BuildEffectiveOutputCategoryMappings(
        IReadOnlyList<SourceCategorySnapshot> upstreamCategories,
        IReadOnlyList<CategoryRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(upstreamCategories);
        ArgumentNullException.ThrowIfNull(rules);

        return BuildEffectiveOutputCategories([.. upstreamCategories.Select(CreateEffectiveCategoryCandidate)], rules);
    }

    public EffectiveCategoryState EvaluateEffectiveState(
        bool isManuallyExcluded,
        string categoryName,
        IReadOnlyList<CategoryRuleDefinition> rules)
    {
        var ruleResult = ruleEvaluator.EvaluateOrdered(categoryName, rules);
        var effectiveDecision = isManuallyExcluded ? CategoryInclusionDecision.Exclude : ruleResult.Decision;

        return new EffectiveCategoryState(
            effectiveDecision,
            ruleResult.Decision,
            ruleResult.MatchedRuleId,
            ruleResult.MatchedRuleSequence,
            ruleResult.MatchedRuleAction,
            ruleResult.MatchedRuleOperator,
            ruleResult.MatchedPattern,
            ruleResult.MatchedRuleCaseSensitive);
    }

    private List<EffectiveOutputCategoryMapping> BuildEffectiveOutputCategories(
        IReadOnlyList<EffectiveCategoryCandidate> effectiveCategoryCandidates,
        IReadOnlyList<CategoryRuleDefinition> rules)
    {
        return [.. effectiveCategoryCandidates
            .Select(category => new
            {
                Category = category,
                State = EvaluateEffectiveState(category.IsExcluded, category.UpstreamCategoryName, rules)
            })
            .Where(result => result.State.EffectiveDecision == CategoryInclusionDecision.Include)
            .GroupBy(
                result => result.Category.CustomCategoryId is int customCategoryId ? $"custom:{customCategoryId}" : $"original:{result.Category.DedicatedOutputCategoryId}",
                StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First().Category;
                var outputId = first.CustomXtreamForgeCategoryId ?? first.DedicatedXtreamForgeCategoryId;
                var displayName = first.CustomDisplayName ?? first.DedicatedDisplayName;
                var sortOrder = group.Min(entry => entry.Category.DedicatedSortOrder);

                return new EffectiveOutputCategoryMapping(
                    outputId,
                    sortOrder,
                    displayName,
                    group.Select(entry => entry.Category.UpstreamCategoryId).Distinct(StringComparer.Ordinal).ToList());
            })
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.XtreamForgeCategoryId)];
    }

    private static EffectiveCategoryCandidate CreateEffectiveCategoryCandidate(SourceCategorySnapshot category) =>
        new(
            category.UpstreamCategoryId,
            category.UpstreamCategoryName,
            category.IsExcluded,
            category.DedicatedOutputCategoryRecordId,
            category.DedicatedXtreamForgeCategoryId,
            category.DedicatedOutputName,
            category.DedicatedOutputSortOrder,
            category.CustomCategoryId,
            category.CustomXtreamForgeCategoryId,
            category.CustomCategoryName);

    private sealed record EffectiveCategoryCandidate(
        string UpstreamCategoryId,
        string UpstreamCategoryName,
        bool IsExcluded,
        int DedicatedOutputCategoryId,
        int DedicatedXtreamForgeCategoryId,
        string DedicatedDisplayName,
        int DedicatedSortOrder,
        int? CustomCategoryId,
        int? CustomXtreamForgeCategoryId,
        string? CustomDisplayName);
}

public sealed record DiscoveredCategory(string UpstreamCategoryId, string UpstreamCategoryName);

public sealed record RewrittenCategory(string CategoryId, string CategoryName, IReadOnlyList<string> IncludedUpstreamCategoryIds);

public sealed record EffectiveOutputCategoryMapping(
    int XtreamForgeCategoryId,
    int SortOrder,
    string DisplayName,
    IReadOnlyList<string> IncludedUpstreamCategoryIds);

public sealed record CustomCategoryUsageSummary(
    int UpstreamCategoryRecordId,
    int SourceId,
    string SourceProtocol,
    string SourceHost,
    int SourcePort,
    ContentType ContentType,
    string UpstreamCategoryId,
    string UpstreamCategoryName);

public sealed record CustomCategorySummary(int Id, int XtreamForgeCategoryId, string DisplayName, int UsageCount);

public sealed record UpstreamCategorySummary(
    int Id,
    string UpstreamCategoryId,
    string UpstreamCategoryName,
    bool IsManuallyExcluded,
    int? CustomCategoryId,
    string? CustomCategoryName,
    int DedicatedXtreamForgeCategoryId,
    string DedicatedOutputName,
    CategoryInclusionDecision EffectiveDecision,
    CategoryInclusionDecision RuleDecision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    CategoryRuleAction? MatchedRuleAction,
    CategoryRuleOperator? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive,
    bool IsEffectivelyIncluded,
    CategoryMappingSelection CurrentMappingSelection);

public sealed record CategoryAdministrationView(
    IReadOnlyList<XtreamSourceSummary> Sources,
    int? SelectedSourceId,
    ContentType SelectedContentType,
    IReadOnlyList<CustomCategorySummary> CustomCategories,
    IReadOnlyList<UpstreamCategorySummary> UpstreamCategories,
    IReadOnlyList<CategoryRuleDefinition> Rules);

public sealed record CategoryConfigurationCommand(
    int UpstreamCategoryRecordId,
    int SelectedSourceId,
    ContentType SelectedContentType,
    CategoryMappingSelection MappingSelection,
    int? CustomCategoryId,
    string? NewCustomCategoryName);

public sealed record CategoryConfigurationResult(
    int SourceId,
    ContentType ContentType,
    CategoryMappingSelection MappingSelection,
    CustomCategorySummary? CustomCategory);
public sealed record CustomCategoryCreateCommand(ContentType ContentType, string? DisplayName);
public sealed record CustomCategoryUpdateCommand(int CustomCategoryId, ContentType ContentType, string DisplayName);

public sealed record CustomCategoryDeleteCommand(int CustomCategoryId, ContentType ContentType);

public sealed record CustomCategoryMutationResult(ContentType ContentType);

public sealed record EffectiveCategoryState(
    CategoryInclusionDecision EffectiveDecision,
    CategoryInclusionDecision RuleDecision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    CategoryRuleAction? MatchedRuleAction,
    CategoryRuleOperator? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive);

public enum CategoryMappingSelection
{
    Disabled = 1,
    Original = 2,
    Custom = 3
}
