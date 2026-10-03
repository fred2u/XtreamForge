using XtreamForge.ApiService.Endpoints.Admin.TmdbRules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbRules;

public class TmdbRulesGetEndpoint(TmdbRuleAdminService tmdbRuleAdminService)
{
    public async Task<IResult> GetAsync(ContentType contentType, CancellationToken cancellationToken = default)
    {
        var rules = await tmdbRuleAdminService.GetAsync(contentType, cancellationToken);

        return TypedResults.Ok(rules.Select(AdminTmdbRuleDto.FromRule));
    }
}
