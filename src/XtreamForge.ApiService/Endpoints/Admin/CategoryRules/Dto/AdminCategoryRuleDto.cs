using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;

public sealed record AdminCategoryRuleDto(
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
    public static AdminCategoryRuleDto FromRule(CategoryRule rule) => new(
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
