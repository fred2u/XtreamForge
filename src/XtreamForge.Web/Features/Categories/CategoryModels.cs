namespace XtreamForge.Web.Features.Categories;

// Numeric values must match the ApiService enums: they are exchanged as numbers in JSON.
// Only the content types managed by the administration UI are declared.
public enum ContentType
{
    Vod = 1,
    Series = 2
}

public enum RuleAction
{
    Include = 1,
    Exclude = 2
}

public enum RuleOperator
{
    StartsWith = 1,
    Contains = 2,
    NotContains = 3,
    NotStartsWith = 4
}

public enum InclusionDecision
{
    Include = 1,
    Exclude = 2
}

public enum CategoryExclusionReason
{
    ManuallyExcluded = 1,
    ProviderDisabled = 2,
    Rule = 3
}

public enum AdminOperationResult
{
    Success,
    NotFound,
    Conflict,

    /// <summary>The API rejected the request (400), for example a custom category that no longer exists.</summary>
    Invalid
}

/// <summary>
/// What a rule filters: provider categories, the items (movies, series) of the categories sent to clients, or the items by their
/// TMDB metadata. Category and item rules are defined per source; TMDB rules are global per content type.
/// </summary>
public enum RuleKind
{
    Category,
    Item,
    Tmdb
}

/// <summary>TMDB metadata a TMDB rule matches.</summary>
public enum TmdbRuleField
{
    Title = 1,
    Genre = 2
}

public sealed record XtreamSourceDto(
    int Id,
    string Protocol,
    string Host,
    int Port)
{
    public string DisplayName => $"{Protocol}://{Host}:{Port}";
}

public sealed record CustomCategoryDto(
    int Id,
    string Name,
    ContentType ContentType);

public sealed record CustomCategoryRequest(
    string Name,
    ContentType ContentType);

/// <summary>An Xtream category mapped to a custom category, with the source it belongs to.</summary>
public sealed record CustomCategoryMapping(
    XtreamSourceDto Source,
    XtreamCategoryDto Category);

/// <summary>
/// An Xtream category as returned by the API, including the decision of the category filtering.
/// <see cref="DecidingRule"/> is the first matching enabled rule; it is null when the category is manually excluded
/// or disabled by the provider, because rules are not evaluated then.
/// </summary>
public sealed record XtreamCategoryDto(
    int Id,
    string XtreamId,
    string Name,
    ContentType ContentType,
    bool IsEnabled,
    bool IsExcluded,
    int? CustomCategoryId,
    string? CustomCategoryName,
    InclusionDecision Decision,
    CategoryExclusionReason? ExclusionReason,
    RuleDto? DecidingRule);

/// <summary>Null values are left unchanged; <see cref="UnassignCustomCategory"/> removes the custom category.</summary>
public sealed record XtreamCategoryPatchRequest(
    bool? IsExcluded = null,
    int? CustomCategoryId = null,
    bool UnassignCustomCategory = false);

/// <summary>
/// A category, item, or TMDB rule. TMDB rules have no source (<see cref="XtreamSourceId"/> is null) and a <see cref="Field"/>.
/// </summary>
public sealed record RuleDto(
    int Id,
    int? XtreamSourceId,
    ContentType ContentType,
    int Sequence,
    RuleAction Action,
    RuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled,
    TmdbRuleField? Field = null);

/// <summary><see cref="Field"/> is required by the TMDB rules and ignored by the other rules.</summary>
public sealed record RuleRequest(
    ContentType ContentType,
    int Sequence,
    RuleAction Action,
    RuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled,
    TmdbRuleField? Field = null);

/// <summary>Every rule of a source and content type, in evaluation order.</summary>
public sealed record RuleOrderRequest(
    ContentType ContentType,
    IReadOnlyList<int> RuleIds);
