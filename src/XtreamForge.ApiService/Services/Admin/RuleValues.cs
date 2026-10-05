using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Rules;

namespace XtreamForge.ApiService.Services.Admin;

/// <summary>Values of a category, item, or TMDB rule set by the administrator.</summary>
public sealed record RuleValues(
    int Sequence,
    RuleAction Action,
    RuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled)
{
    public void ApplyTo(IRule rule)
    {
        rule.Sequence = Sequence;
        rule.Action = Action;
        rule.Operator = Operator;
        rule.Pattern = Pattern;
        rule.CaseSensitive = CaseSensitive;
        rule.IsEnabled = IsEnabled;
    }
}
