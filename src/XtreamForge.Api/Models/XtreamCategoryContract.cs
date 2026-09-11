using System.Text.Json.Serialization;

namespace XtreamForge.Api.Models;

public sealed record XtreamUpstreamCategoryDto(
    [property: JsonPropertyName("category_id")] string? CategoryId,
    [property: JsonPropertyName("category_name")] string? CategoryName);

public sealed record XtreamCategoryResponseDto(
    [property: JsonPropertyName("category_id")] string CategoryId,
    [property: JsonPropertyName("category_name")] string CategoryName);
