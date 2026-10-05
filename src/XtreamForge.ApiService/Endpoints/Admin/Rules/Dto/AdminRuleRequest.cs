using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.Rules.Dto;

/// <summary>
/// A category, item, or TMDB rule to create or update. The content type is ignored on update;
/// <see cref="Field"/> is required by the TMDB rules and ignored by the other rules.
/// </summary>
public sealed record AdminRuleRequest(
    ContentType ContentType,
    int Sequence,
    RuleAction Action,
    RuleOperator Operator,
    string? Pattern,
    bool CaseSensitive,
    bool IsEnabled,
    TmdbRuleField? Field = null)
{
    // pattern column length of the rule tables
    public const int PatternMaxLength = 255;

    /// <summary>Returns the validation errors, or null when the request is valid.</summary>
    public Dictionary<string, string[]>? Validate(bool requiresField)
    {
        var errors = new Dictionary<string, string[]>();

        if (ContentType == ContentType.Undefined || !Enum.IsDefined(ContentType))
            errors[nameof(ContentType)] = ["The content type is not valid."];
        if (requiresField && (Field is not { } field || !Enum.IsDefined(field)))
            errors[nameof(Field)] = ["The field is not valid."];
        if (!Enum.IsDefined(Action))
            errors[nameof(Action)] = ["The action is not valid."];
        if (!Enum.IsDefined(Operator))
            errors[nameof(Operator)] = ["The operator is not valid."];
        if (string.IsNullOrWhiteSpace(Pattern) || Pattern.Length > PatternMaxLength)
            errors[nameof(Pattern)] = [$"The pattern is required and must not exceed {PatternMaxLength} characters."];

        return errors.Count == 0 ? null : errors;
    }

    public RuleValues ToValues() => new(Sequence, Action, Operator, Pattern ?? string.Empty, CaseSensitive, IsEnabled);
}
