using Microsoft.AspNetCore.Http.HttpResults;
using XtreamForge.ApiService.Endpoints.Admin.XtreamCategories.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.XtreamCategories;

public class XtreamCategoryPatchEndpoint(XtreamCategoryAdminService categoryAdminService)
{
    public async Task<IResult> PatchAsync(int id, AdminXtreamCategoryPatchRequest request, CancellationToken cancellationToken = default)
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
