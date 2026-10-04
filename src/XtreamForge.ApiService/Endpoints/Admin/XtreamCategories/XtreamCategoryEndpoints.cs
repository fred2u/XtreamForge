using Microsoft.AspNetCore.Http.HttpResults;
using XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;
using XtreamForge.ApiService.Endpoints.Admin.XtreamCategories.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.XtreamCategories;

public static class XtreamCategoryEndpoints
{
    public static async Task<IResult> GetAsync(
        int sourceId,
        ContentType contentType,
        XtreamCategoryAdminService categoryAdminService,
        SourceAdminService sourceAdminService,
        CancellationToken cancellationToken = default)
    {
        var source = await sourceAdminService.FindAsync(sourceId, cancellationToken);
        if (source is null)
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

    public static async Task<IResult> PatchAsync(int id, AdminXtreamCategoryPatchRequest request, XtreamCategoryAdminService categoryAdminService, CancellationToken cancellationToken = default)
    {
        if (request.UnassignCustomCategory && request.CustomCategoryId is not null)
        {
            return CustomCategoryError("Assigning and unassigning a custom category in the same request is not allowed.");
        }

        if (request.CustomCategoryId is <= 0)
        {
            return CustomCategoryError("The custom category id must be positive. Use unassignCustomCategory to remove the custom category.");
        }

        var result = await categoryAdminService.PatchAsync(id, request.IsExcluded, request.CustomCategoryId, request.UnassignCustomCategory, cancellationToken);

        return result switch
        {
            XtreamCategoryPatchResult.Updated => TypedResults.NoContent(),
            XtreamCategoryPatchResult.CustomCategoryNotFound => CustomCategoryError($"Custom category {request.CustomCategoryId} does not exist."),
            _ => TypedResults.NotFound()
        };
    }

    private static ValidationProblem CustomCategoryError(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(AdminXtreamCategoryPatchRequest.CustomCategoryId)] = [message]
        });
}
