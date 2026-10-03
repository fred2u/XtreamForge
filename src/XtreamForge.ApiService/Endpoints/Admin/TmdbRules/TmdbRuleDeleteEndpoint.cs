using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbRules;

public class TmdbRuleDeleteEndpoint(TmdbRuleAdminService tmdbRuleAdminService)
{
    public async Task<IResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await tmdbRuleAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
