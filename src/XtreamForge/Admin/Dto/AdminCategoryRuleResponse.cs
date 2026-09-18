namespace XtreamForge.Admin.Dto;

public sealed record AdminCategoryRuleResponse(
    int Id,
    int Sequence,
    string Action,
    string Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);
