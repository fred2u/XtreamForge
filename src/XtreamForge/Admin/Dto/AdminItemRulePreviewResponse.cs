namespace XtreamForge.Admin.Dto;

public sealed record AdminItemRulePreviewResponse(
    string ItemName,
    string Decision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    string? MatchedRuleField,
    string? MatchedRuleAction,
    string? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive);
