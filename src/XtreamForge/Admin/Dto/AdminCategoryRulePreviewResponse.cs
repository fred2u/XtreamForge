namespace XtreamForge.Admin.Dto;

public sealed record AdminCategoryRulePreviewResponse(
    string CategoryName,
    string Decision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    string? MatchedRuleAction,
    string? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive);
