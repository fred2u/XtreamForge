namespace XtreamForge.Admin.Dto;

public sealed record AdminCustomCategoryUsageResponse(
    int UpstreamCategoryRecordId,
    int SourceId,
    string SourceProtocol,
    string SourceHost,
    int SourcePort,
    string ContentType,
    string UpstreamCategoryId,
    string UpstreamCategoryName);
