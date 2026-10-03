using XtreamForge.ApiService.Endpoints.Admin.TmdbRules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbRules;

public class TmdbRulesOrderPutEndpoint(TmdbRuleAdminService tmdbRuleAdminService)
{
    public async Task<IResult> PutAsync(AdminTmdbRuleOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ContentType == ContentType.Undefined || !Enum.IsDefined(request.ContentType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminTmdbRuleOrderRequest.ContentType)] = ["The content type is not valid."]
            });
        }

        var (result, rules) = await tmdbRuleAdminService.ReorderAsync(request.ContentType, request.RuleIds ?? [], cancellationToken);

        return result == RuleReorderResult.Reordered
            ? TypedResults.Ok(rules.Select(AdminTmdbRuleDto.FromRule))
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminTmdbRuleOrderRequest.RuleIds)] = ["The rule ids must list every rule of the content type exactly once."]
            });
    }
}
