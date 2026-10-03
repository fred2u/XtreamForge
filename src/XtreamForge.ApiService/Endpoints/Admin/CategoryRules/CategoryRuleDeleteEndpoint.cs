using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.CategoryRules;

public class CategoryRuleDeleteEndpoint(CategoryRuleAdminService categoryRuleAdminService)
{
    public async Task<IResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await categoryRuleAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
