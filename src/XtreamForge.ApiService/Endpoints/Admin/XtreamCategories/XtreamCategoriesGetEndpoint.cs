using XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;
using XtreamForge.ApiService.Endpoints.Admin.XtreamCategories.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.XtreamCategories;

public class XtreamCategoriesGetEndpoint(XtreamCategoryAdminService categoryAdminService, SourceAdminService sourceAdminService)
{
    public async Task<IResult> GetAsync(int sourceId, ContentType contentType, CancellationToken cancellationToken = default)
    {
        var sources = await sourceAdminService.FindAsync(sourceId, cancellationToken);
        if (sources is null)
        {
            return TypedResults.NotFound();
        }

        var evaluations = await categoryAdminService.GetBySourceAsync(sourceId, contentType, cancellationToken);

        var dtos = evaluations.Select(e => new AdminXtreamCategoryDto(
            e.Category.Id,
            e.Category.XtreamId,
            e.Category.Name,
            e.Category.ContentType,
            e.Category.IsEnabled,
            e.Category.IsExcluded,
            e.Category.CustomCategoryId,
            e.Category.CustomCategory?.Name,
            e.Decision,
            e.ExclusionReason,
            e.DecidingRule is null ? null : AdminCategoryRuleDto.FromRule(e.DecidingRule)));

        return TypedResults.Ok(dtos);
    }
}
