using XtreamForge.ApiService.Endpoints.Admin.Rules.Dto;
using XtreamForge.ApiService.Services;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.XtreamCategories.Dto;

/// <summary>
/// An Xtream category with the decision the category filtering makes for it.
/// <see cref="ExclusionReason"/> is set only when <see cref="Decision"/> is Exclude.
/// <see cref="DecidingRule"/> is the first matching enabled rule that made the decision (include or exclude);
/// it is null when the category is manually excluded or disabled by the provider, because rules are not evaluated then.
/// </summary>
public sealed record AdminXtreamCategoryDto(
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
    AdminRuleDto? DecidingRule);
