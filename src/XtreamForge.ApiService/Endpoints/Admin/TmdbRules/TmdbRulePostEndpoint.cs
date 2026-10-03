using XtreamForge.ApiService.Endpoints.Admin.TmdbRules.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbRules;

public class TmdbRulePostEndpoint(TmdbRuleAdminService tmdbRuleAdminService)
{
    public async Task<IResult> PostAsync(AdminTmdbRuleRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Validate() is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (rule, sequenceConflict) = await tmdbRuleAdminService.CreateAsync(request.ContentType, request.ToValues(), cancellationToken);
        if (sequenceConflict || rule is null)
        {
            return TypedResults.Conflict();
        }

        return TypedResults.Created($"/api/admin/tmdb-rules/{rule.Id}", AdminTmdbRuleDto.FromRule(rule));
    }
}
