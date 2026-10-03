using XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings;

public class StreamTmdbMappingPatchEndpoint(StreamTmdbMappingAdminService streamTmdbMappingAdminService)
{
    public async Task<IResult> PatchAsync(int id, AdminStreamTmdbMappingPatchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TmdbId <= 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminStreamTmdbMappingPatchRequest.TmdbId)] = ["The TMDB ID must be a positive number."]
            });
        }

        var found = await streamTmdbMappingAdminService.SetTmdbIdAsync(id, request.TmdbId, cancellationToken);

        return found
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
