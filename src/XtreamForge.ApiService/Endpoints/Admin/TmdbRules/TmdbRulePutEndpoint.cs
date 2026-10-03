using XtreamForge.ApiService.Endpoints.Admin.TmdbRules.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbRules;

public class TmdbRulePutEndpoint(TmdbRuleAdminService tmdbRuleAdminService)
{
    public async Task<IResult> PutAsync(int id, AdminTmdbRuleRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Validate() is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (found, sequenceConflict) = await tmdbRuleAdminService.UpdateAsync(id, request.ToValues(), cancellationToken);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        return sequenceConflict ? TypedResults.Conflict() : TypedResults.NoContent();
    }
}
