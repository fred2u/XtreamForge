using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.ItemRules.Dto;

/// <summary>
/// The complete list of the rules of a source and content type, in the desired evaluation order (first = highest priority).
/// </summary>
public sealed record AdminItemRuleOrderRequest(
    ContentType ContentType,
    IReadOnlyList<int>? RuleIds);
