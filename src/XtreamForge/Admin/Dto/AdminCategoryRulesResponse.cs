namespace XtreamForge.Admin.Dto;

public sealed record AdminCategoryRulesResponse(
    int? SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<AdminCategoryRuleResponse> Rules);
