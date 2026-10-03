using XtreamForge.ApiService.Endpoints.Admin.CustomCategories.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.CustomCategories;

public class CustomCategoryPutEndpoint(CustomCategoryAdminService customCategoryAdminService)
{
    public async Task<IResult> PutAsync(int id, AdminCustomCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var (found, nameConflict) = await customCategoryAdminService.UpdateAsync(id, request.Name, cancellationToken);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        if (nameConflict)
        {
            return TypedResults.Conflict();
        }

        return TypedResults.NoContent();
    }
}
