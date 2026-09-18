namespace XtreamForge.Admin.Dto;

public sealed record AdminItemRuleResponse(
    int Id,
    int Sequence,
    string Field,
    string Action,
    string Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);
