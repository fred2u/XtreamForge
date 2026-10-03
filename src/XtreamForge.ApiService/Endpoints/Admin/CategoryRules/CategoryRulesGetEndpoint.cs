using XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.CategoryRules;

public class CategoryRulesGetEndpoint(CategoryRuleAdminService categoryRuleAdminService, SourceAdminService sourceAdminService)
{
    public async Task<IResult> GetAsync(int sourceId, ContentType contentType, CancellationToken cancellationToken = default)
    {
        var sources = await sourceAdminService.FindAsync(sourceId, cancellationToken);
        if (sources is null)
        {
            return TypedResults.NotFound();
        }

        var rules = await categoryRuleAdminService.GetBySourceAsync(sourceId, contentType, cancellationToken);

        var dtos = rules.Select(r => new AdminCategoryRuleDto(
            r.Id,
            r.XtreamSourceId,
            r.ContentType,
            r.Sequence,
            r.Action,
            r.Operator,
            r.Pattern,
            r.CaseSensitive,
            r.IsEnabled));

        return TypedResults.Ok(dtos);
    }
}
