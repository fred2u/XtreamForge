using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;

namespace XtreamForge.ApiService.Endpoints.Admin.ItemRules.Dto;

public sealed record AdminItemRuleDto(
    int Id,
    int XtreamSourceId,
    ContentType ContentType,
    int Sequence,
    RuleAction Action,
    RuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled)
{
    public static AdminItemRuleDto FromRule(ItemRule rule) => new(
        rule.Id,
        rule.XtreamSourceId,
        rule.ContentType,
        rule.Sequence,
        rule.Action,
        rule.Operator,
        rule.Pattern,
        rule.CaseSensitive,
        rule.IsEnabled);
}
