using XtreamForge.ApiService.Endpoints.Admin.ItemRules.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.ItemRules;

public class ItemRulePutEndpoint(ItemRuleAdminService itemRuleAdminService)
{
    public async Task<IResult> PutAsync(int id, AdminItemRuleRequest request, CancellationToken cancellationToken = default)
    {
        var (found, sequenceConflict) = await itemRuleAdminService.UpdateAsync(
            id,
            request.Sequence,
            request.Action,
            request.Operator,
            request.Pattern,
            request.CaseSensitive,
            request.IsEnabled,
            cancellationToken);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        if (sequenceConflict)
        {
            return TypedResults.Conflict();
        }

        return TypedResults.NoContent();
    }
}
