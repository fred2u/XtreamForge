namespace XtreamForge.Admin.Dto;

public sealed record AdminItemRuleRequest(
    int SelectedSourceId,
    string SelectedContentType,
    string Field,
    string Action,
    string Operator,
    string? Pattern,
    bool CaseSensitive,
    bool IsEnabled);
