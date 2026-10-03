using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.ItemRules.Dto;

public sealed record AdminItemRuleRequest(
    ContentType ContentType,
    int Sequence,
    RuleAction Action,
    RuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);
