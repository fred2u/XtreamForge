namespace XtreamForge.Admin.Dto;

public sealed record AdminStatusResponse(
    string ApplicationName,
    string ApplicationVersion,
    string Status,
    string DatabaseStatus,
    string DatabaseDetails,
    int SourceCount,
    int SourceCategoryCount,
    int RuleCount,
    int CustomCategoryCount,
    int ItemRuleCount,
    int KnownTmdbMappingCount);
