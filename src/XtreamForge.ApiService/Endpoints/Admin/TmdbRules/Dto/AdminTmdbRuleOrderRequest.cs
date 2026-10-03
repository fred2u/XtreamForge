using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbRules.Dto;

/// <summary>
/// The complete list of the TMDB rules of a content type, in the desired evaluation order (first = highest priority).
/// </summary>
public sealed record AdminTmdbRuleOrderRequest(
    ContentType ContentType,
    IReadOnlyList<int>? RuleIds);
