namespace XtreamForge.ApiService.Endpoints.Admin.Sources.Dto;

/// <summary>
/// Partial update of a source. Omitted (null) values are left unchanged.
/// </summary>
/// <param name="IsRecommendationsCategoryEnabled">Whether the recommendations virtual category is returned to the clients of the source.</param>
/// <param name="IsPopularCategoryEnabled">Whether the popular virtual category is returned to the clients of the source.</param>
public sealed record XtreamSourcePatchRequest(
    bool? IsRecommendationsCategoryEnabled,
    bool? IsPopularCategoryEnabled);
