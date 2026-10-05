using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Rules;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Endpoints.Admin.Rules.Dto;

/// <summary>
/// A category, item, or TMDB rule. Category and item rules have a source and no <see cref="Field"/>;
/// TMDB rules have no source (<see cref="XtreamSourceId"/> is null) and a <see cref="Field"/>.
/// </summary>
public sealed record AdminRuleDto(
    int Id,
    int? XtreamSourceId,
    ContentType ContentType,
    int Sequence,
    RuleAction Action,
    RuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled,
    TmdbRuleField? Field)
{
    public static AdminRuleDto FromRule(ISourceRule rule) => From(rule, rule.XtreamSourceId, null);

    public static AdminRuleDto FromRule(TmdbRule rule) => From(rule, null, rule.Field);

    private static AdminRuleDto From(IRule rule, int? xtreamSourceId, TmdbRuleField? field) => new(
        rule.Id,
        xtreamSourceId,
        rule.ContentType,
        rule.Sequence,
        rule.Action,
        rule.Operator,
        rule.Pattern,
        rule.CaseSensitive,
        rule.IsEnabled,
        field);
}
