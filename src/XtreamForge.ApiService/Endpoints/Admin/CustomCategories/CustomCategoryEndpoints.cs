using XtreamForge.ApiService.Endpoints.Admin.CustomCategories.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.CustomCategories;

public static class CustomCategoryEndpoints
{
    public static async Task<IResult> GetAsync(ContentType contentType, CustomCategoryAdminService customCategoryAdminService, CancellationToken cancellationToken = default)
    {
        var categories = await customCategoryAdminService.GetAllAsync(contentType, cancellationToken);

        var dtos = categories.Select(c => new AdminCustomCategoryDto(c.Id, c.Name, c.ContentType));

        return TypedResults.Ok(dtos);
    }

    public static async Task<IResult> PostAsync(AdminCustomCategoryRequest request, CustomCategoryAdminService customCategoryAdminService, CancellationToken cancellationToken = default)
    {
        var (category, nameConflict) = await customCategoryAdminService.CreateAsync(request.Name, request.ContentType, cancellationToken);

        if (nameConflict || category is null)
        {
            return TypedResults.Conflict();
        }

        var dto = new AdminCustomCategoryDto(category.Id, category.Name, category.ContentType);

        return TypedResults.Created($"/api/admin/custom-categories/{category.Id}", dto);
    }

    public static async Task<IResult> PutAsync(int id, AdminCustomCategoryRequest request, CustomCategoryAdminService customCategoryAdminService, CancellationToken cancellationToken = default)
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

    public static async Task<IResult> DeleteAsync(int id, CustomCategoryAdminService customCategoryAdminService, CancellationToken cancellationToken = default)
    {
        var deleted = await customCategoryAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
