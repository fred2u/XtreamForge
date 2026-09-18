namespace XtreamForge.Admin.Dto;

public sealed record AdminUpstreamCategoryResponse(
    int Id,
    string UpstreamCategoryId,
    string UpstreamCategoryName,
    bool IsManuallyExcluded,
    int? CustomCategoryId,
    string? CustomCategoryName,
    int DedicatedXtreamForgeCategoryId,
    string DedicatedOutputName,
    string EffectiveDecision,
    string RuleDecision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    string? MatchedRuleAction,
    string? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive,
    bool IsEffectivelyIncluded,
    string CurrentMappingSelection);
