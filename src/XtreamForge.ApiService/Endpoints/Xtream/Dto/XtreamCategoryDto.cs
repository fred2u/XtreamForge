using System.Text.Json.Serialization;

namespace XtreamForge.ApiService.Endpoints.Xtream.Dto;

public sealed record XtreamCategoryDto(
    [property: JsonPropertyName("category_id")] string CategoryId,
    [property: JsonPropertyName("category_name")] string CategoryName);
