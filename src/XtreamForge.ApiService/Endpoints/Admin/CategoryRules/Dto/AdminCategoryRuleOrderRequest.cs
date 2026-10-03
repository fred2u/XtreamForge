using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;

/// <summary>
/// The complete list of the rules of a source and content type, in the desired evaluation order (first = highest priority).
/// </summary>
public sealed record AdminCategoryRuleOrderRequest(
    ContentType ContentType,
    IReadOnlyList<int>? RuleIds);
