namespace XtreamForge.Admin.Dto;

public sealed record AdminCategoryRuleRequest(
    int SelectedSourceId,
    string SelectedContentType,
    string Action,
    string Operator,
    string? Pattern,
    bool CaseSensitive,
    bool IsEnabled);
