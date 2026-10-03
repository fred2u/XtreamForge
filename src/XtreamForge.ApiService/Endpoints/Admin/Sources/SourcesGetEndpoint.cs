using XtreamForge.ApiService.Endpoints.Admin.Sources.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.Sources;

public class SourcesGetEndpoint(SourceAdminService sourceAdminService)
{
    public async Task<IResult> GetAsync(CancellationToken cancellationToken = default)
    {
        var summaries = await sourceAdminService.GetSummariesAsync(cancellationToken);

        var dtos = summaries.Select(s => new XtreamSourceSummaryDto(
            s.Source.Id,
            s.Source.Protocol,
            s.Source.Host,
            s.Source.Port,
            ToDto(s.Vod),
            ToDto(s.Series)));

        return TypedResults.Ok(dtos);
    }

    private static XtreamSourceContentSummaryDto ToDto(SourceContentSummary summary) => new(
        summary.Categories,
        summary.EffectiveCategories,
        summary.ManuallyExcludedCategories,
        summary.ProviderDisabledCategories,
        summary.RuleExcludedCategories,
        summary.MappedCategories,
        summary.CategoryRules,
        summary.ItemRules,
        summary.TmdbMappings);
}
