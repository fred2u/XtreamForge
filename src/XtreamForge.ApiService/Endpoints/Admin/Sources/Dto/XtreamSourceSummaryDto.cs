namespace XtreamForge.ApiService.Endpoints.Admin.Sources.Dto;

public sealed record XtreamSourceSummaryDto(
    int Id,
    string Protocol,
    string Host,
    int Port,
    XtreamSourceContentSummaryDto Vod,
    XtreamSourceContentSummaryDto Series);

public sealed record XtreamSourceContentSummaryDto(
    int Categories,
    int EffectiveCategories,
    int ManuallyExcludedCategories,
    int ProviderDisabledCategories,
    int RuleExcludedCategories,
    int MappedCategories,
    int CategoryRules,
    int ItemRules,
    int TmdbMappings);
