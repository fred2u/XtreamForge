using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbRules.Dto;

/// <summary>A TMDB rule to create or update; the content type is ignored on update.</summary>
public sealed record AdminTmdbRuleRequest(
    ContentType ContentType,
    int Sequence,
    TmdbRuleField Field,
    RuleAction Action,
    RuleOperator Operator,
    string? Pattern,
    bool CaseSensitive,
    bool IsEnabled)
{
    // tmdb_rules.pattern column length
    public const int PatternMaxLength = 255;

    /// <summary>Returns the validation errors, or null when the request is valid.</summary>
    public Dictionary<string, string[]>? Validate()
    {
        var errors = new Dictionary<string, string[]>();

        if (ContentType == ContentType.Undefined || !Enum.IsDefined(ContentType))
            errors[nameof(ContentType)] = ["The content type is not valid."];
        if (!Enum.IsDefined(Field))
            errors[nameof(Field)] = ["The field is not valid."];
        if (!Enum.IsDefined(Action))
            errors[nameof(Action)] = ["The action is not valid."];
        if (!Enum.IsDefined(Operator))
            errors[nameof(Operator)] = ["The operator is not valid."];
        if (string.IsNullOrWhiteSpace(Pattern) || Pattern.Length > PatternMaxLength)
            errors[nameof(Pattern)] = [$"The pattern is required and must not exceed {PatternMaxLength} characters."];

        return errors.Count == 0 ? null : errors;
    }

    public TmdbRuleValues ToValues() => new(Sequence, Field, Action, Operator, Pattern ?? string.Empty, CaseSensitive, IsEnabled);
}
