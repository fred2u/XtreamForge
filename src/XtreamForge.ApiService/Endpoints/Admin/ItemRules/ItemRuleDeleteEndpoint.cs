using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.ItemRules;

public class ItemRuleDeleteEndpoint(ItemRuleAdminService itemRuleAdminService)
{
    public async Task<IResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await itemRuleAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
