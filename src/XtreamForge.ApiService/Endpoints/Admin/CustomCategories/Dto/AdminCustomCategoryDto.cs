using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.CustomCategories.Dto;

public sealed record AdminCustomCategoryDto(
    int Id,
    string Name,
    ContentType ContentType);
