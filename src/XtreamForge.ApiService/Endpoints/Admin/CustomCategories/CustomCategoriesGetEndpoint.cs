using XtreamForge.ApiService.Endpoints.Admin.CustomCategories.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.CustomCategories;

public class CustomCategoriesGetEndpoint(CustomCategoryAdminService customCategoryAdminService)
{
    public async Task<IResult> GetAsync(ContentType contentType, CancellationToken cancellationToken = default)
    {
        var categories = await customCategoryAdminService.GetAllAsync(contentType, cancellationToken);

        var dtos = categories.Select(c => new AdminCustomCategoryDto(c.Id, c.Name, c.ContentType));

        return TypedResults.Ok(dtos);
    }
}
