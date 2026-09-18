namespace XtreamForge.Admin.Dto;

public sealed record AdminCategoryMappingMutationResponse(
    int SourceId,
    string ContentType,
    string MappingValue,
    AdminCustomCategoryResponse? CustomCategory);
