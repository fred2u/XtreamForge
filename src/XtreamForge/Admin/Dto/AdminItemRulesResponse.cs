namespace XtreamForge.Admin.Dto;

public sealed record AdminItemRulesResponse(
    IReadOnlyList<AdminSourceResponse> Sources,
    int? SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<AdminItemRuleResponse> Rules);
