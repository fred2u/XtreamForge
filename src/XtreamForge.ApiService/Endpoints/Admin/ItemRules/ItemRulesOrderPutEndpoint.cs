using XtreamForge.ApiService.Endpoints.Admin.ItemRules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.ItemRules;

public class ItemRulesOrderPutEndpoint(ItemRuleAdminService itemRuleAdminService)
{
    public async Task<IResult> PutAsync(int sourceId, AdminItemRuleOrderRequest request, CancellationToken cancellationToken = default)
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
}
