using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbInfos;

public class TmdbInfoGetEndpoint(TmdbInfoAdminService tmdbInfoAdminService, IOptions<TmdbOptions> tmdbOptions)
{
    public async Task<IResult> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var evaluation = await tmdbInfoAdminService.GetAsync(id, cancellationToken);

        return evaluation is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(AdminTmdbInfoDetailsDto.From(evaluation, tmdbOptions.Value.ImageBaseUrl));
    }
}
