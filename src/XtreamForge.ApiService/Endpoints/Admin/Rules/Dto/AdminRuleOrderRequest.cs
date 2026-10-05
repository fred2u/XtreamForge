using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.Rules.Dto;

/// <summary>
/// The complete list of the rules of a scope (a content type, of a source for the category and item rules),
/// in the desired evaluation order (first = highest priority).
/// </summary>
public sealed record AdminRuleOrderRequest(
    ContentType ContentType,
    IReadOnlyList<int>? RuleIds);
