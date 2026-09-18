namespace XtreamForge.Admin.Dto;

public sealed record AdminCategoryMappingRequest(
    int SelectedSourceId,
    string SelectedContentType,
    string MappingSelection,
    int? CustomCategoryId,
    string? NewCustomCategoryName);
