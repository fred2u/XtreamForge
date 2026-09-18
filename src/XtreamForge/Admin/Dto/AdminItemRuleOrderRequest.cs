namespace XtreamForge.Admin.Dto;

public sealed record AdminItemRuleOrderRequest(
    int SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<int> OrderedRuleIds);
