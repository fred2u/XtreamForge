using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbRules.Dto;

public sealed record AdminTmdbRuleDto(
    int Id,
    ContentType ContentType,
    int Sequence,
    TmdbRuleField Field,
    RuleAction Action,
    RuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled)
{
    public static AdminTmdbRuleDto FromRule(TmdbRule rule) => new(
        rule.Id,
        rule.ContentType,
        rule.Sequence,
        rule.Field,
        rule.Action,
        rule.Operator,
        rule.Pattern,
        rule.CaseSensitive,
        rule.IsEnabled);
}
