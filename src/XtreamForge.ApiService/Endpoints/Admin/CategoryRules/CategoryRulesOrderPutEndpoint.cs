using XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.CategoryRules;

public class CategoryRulesOrderPutEndpoint(CategoryRuleAdminService categoryRuleAdminService)
{
    public async Task<IResult> PutAsync(int sourceId, AdminCategoryRuleOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ContentType == ContentType.Undefined || !Enum.IsDefined(request.ContentType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminCategoryRuleOrderRequest.ContentType)] = ["The content type is not valid."]
            });
        }

        var (result, rules) = await categoryRuleAdminService.ReorderAsync(sourceId, request.ContentType, request.RuleIds ?? [], cancellationToken);

        return result switch
        {
            RuleReorderResult.Reordered => TypedResults.Ok(rules.Select(AdminCategoryRuleDto.FromRule)),
            RuleReorderResult.InvalidOrder => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminCategoryRuleOrderRequest.RuleIds)] = ["The rule ids must list every rule of the source and content type exactly once."]
            }),
            _ => TypedResults.NotFound()
        };
    }
}
