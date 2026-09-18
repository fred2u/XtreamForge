namespace XtreamForge.Admin.Dto;

public sealed record AdminCategoriesResponse(
    IReadOnlyList<AdminSourceResponse> Sources,
    int? SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<AdminCustomCategoryResponse> CustomCategories,
    IReadOnlyList<AdminUpstreamCategoryResponse> UpstreamCategories);
