using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbInfos;

public class TmdbInfoPatchEndpoint(TmdbInfoAdminService tmdbInfoAdminService)
{
    public async Task<IResult> PatchAsync(int id, AdminTmdbInfoPatchRequest request, CancellationToken cancellationToken = default)
    {
        var found = await tmdbInfoAdminService.SetExcludedAsync(id, request.IsExcluded, cancellationToken);

        return found
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
