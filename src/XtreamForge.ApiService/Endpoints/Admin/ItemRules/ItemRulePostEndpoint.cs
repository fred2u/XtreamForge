using XtreamForge.ApiService.Endpoints.Admin.ItemRules.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.ItemRules;

public class ItemRulePostEndpoint(ItemRuleAdminService itemRuleAdminService)
{
    public async Task<IResult> PostAsync(int sourceId, AdminItemRuleRequest request, CancellationToken cancellationToken = default)
    {
        var (rule, sequenceConflict) = await itemRuleAdminService.CreateAsync(
            sourceId,
            request.ContentType,
            request.Sequence,
            request.Action,
            request.Operator,
            request.Pattern,
            request.CaseSensitive,
            request.IsEnabled,
            cancellationToken);

        if (sequenceConflict)
        {
            return TypedResults.Conflict();
        }

        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        var dto = new AdminItemRuleDto(
            rule.Id,
            rule.XtreamSourceId,
            rule.ContentType,
            rule.Sequence,
            rule.Action,
            rule.Operator,
            rule.Pattern,
            rule.CaseSensitive,
            rule.IsEnabled);

        return TypedResults.Created($"/api/admin/sources/{sourceId}/item-rules/{rule.Id}", dto);
    }
}
