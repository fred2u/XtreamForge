using XtreamForge.ApiService.Endpoints.Admin.ItemRules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.ItemRules;

public static class ItemRuleEndpoints
{
    public static async Task<IResult> GetAsync(
        int sourceId,
        ContentType contentType,
        ItemRuleAdminService itemRuleAdminService,
        SourceAdminService sourceAdminService,
        CancellationToken cancellationToken = default)
    {
        var source = await sourceAdminService.FindAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return TypedResults.NotFound();
        }

        var rules = await itemRuleAdminService.GetBySourceAsync(sourceId, contentType, cancellationToken);

        return TypedResults.Ok(rules.Select(AdminItemRuleDto.FromRule));
    }

    public static async Task<IResult> PostAsync(int sourceId, AdminItemRuleRequest request, ItemRuleAdminService itemRuleAdminService, CancellationToken cancellationToken = default)
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

        return TypedResults.Created($"/api/admin/sources/{sourceId}/item-rules/{rule.Id}", AdminItemRuleDto.FromRule(rule));
    }

    public static async Task<IResult> PutAsync(int id, AdminItemRuleRequest request, ItemRuleAdminService itemRuleAdminService, CancellationToken cancellationToken = default)
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

    public static async Task<IResult> PutOrderAsync(int sourceId, AdminItemRuleOrderRequest request, ItemRuleAdminService itemRuleAdminService, CancellationToken cancellationToken = default)
    {
        if (request.ContentType == ContentType.Undefined || !Enum.IsDefined(request.ContentType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminItemRuleOrderRequest.ContentType)] = ["The content type is not valid."]
            });
        }

        var (result, rules) = await itemRuleAdminService.ReorderAsync(sourceId, request.ContentType, request.RuleIds ?? [], cancellationToken);

        return result switch
        {
            RuleReorderResult.Reordered => TypedResults.Ok(rules.Select(AdminItemRuleDto.FromRule)),
            RuleReorderResult.InvalidOrder => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminItemRuleOrderRequest.RuleIds)] = ["The rule ids must list every rule of the source and content type exactly once."]
            }),
            _ => TypedResults.NotFound()
        };
    }

    public static async Task<IResult> DeleteAsync(int id, ItemRuleAdminService itemRuleAdminService, CancellationToken cancellationToken = default)
    {
        var deleted = await itemRuleAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
