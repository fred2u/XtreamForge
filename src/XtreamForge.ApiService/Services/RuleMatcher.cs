using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services;

/// <summary>Name matching shared by the category and item rules.</summary>
public static class RuleMatcher
{
    public static bool IsMatch(string name, RuleOperator @operator, string pattern, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return @operator switch
        {
            RuleOperator.StartsWith => name.StartsWith(pattern, comparison),
            RuleOperator.Contains => name.Contains(pattern, comparison),
            RuleOperator.NotContains => !name.Contains(pattern, comparison),
            RuleOperator.NotStartsWith => !name.StartsWith(pattern, comparison),
            _ => throw new NotSupportedException($"Unsupported operator: {@operator}")
        };
    }
}
