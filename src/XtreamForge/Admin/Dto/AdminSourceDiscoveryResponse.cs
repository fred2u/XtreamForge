namespace XtreamForge.Admin.Dto;

public sealed record AdminSourceDiscoveryResponse(
    int SourceId,
    int VodCategoryCount,
    int SeriesCategoryCount);
