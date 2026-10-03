using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.CustomCategories.Dto;

public sealed record AdminCustomCategoryRequest(
    string Name,
    ContentType ContentType);
