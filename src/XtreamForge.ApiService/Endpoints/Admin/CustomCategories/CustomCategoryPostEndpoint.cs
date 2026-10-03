using XtreamForge.ApiService.Endpoints.Admin.CustomCategories.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.CustomCategories;

public class CustomCategoryPostEndpoint(CustomCategoryAdminService customCategoryAdminService)
{
    public async Task<IResult> PostAsync(AdminCustomCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var (category, nameConflict) = await customCategoryAdminService.CreateAsync(request.Name, request.ContentType, cancellationToken);

        if (nameConflict)
        {
            return TypedResults.Conflict();
        }

        var dto = new AdminCustomCategoryDto(category!.Id, category.Name, category.ContentType);

        return TypedResults.Created($"/api/admin/custom-categories/{category.Id}", dto);
    }
}
