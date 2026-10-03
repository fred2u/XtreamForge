namespace XtreamForge.ApiService.Endpoints.Admin.XtreamCategories.Dto;

/// <summary>
/// Partial update of an Xtream category. Omitted (null) values are left unchanged.
/// </summary>
/// <param name="IsExcluded">New manual exclusion state.</param>
/// <param name="CustomCategoryId">Custom category to assign (a positive id).</param>
/// <param name="UnassignCustomCategory">True removes the custom category; cannot be combined with <paramref name="CustomCategoryId"/>.</param>
public sealed record AdminXtreamCategoryPatchRequest(
    bool? IsExcluded,
    int? CustomCategoryId,
    bool UnassignCustomCategory = false);
