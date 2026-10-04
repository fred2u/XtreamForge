using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings;

public static class StreamTmdbMappingEndpoints
{
    public static async Task<IResult> GetListAsync(
        int sourceId,
        [AsParameters] StreamTmdbMappingListQuery query,
        StreamTmdbMappingAdminService streamTmdbMappingAdminService,
        IOptions<TmdbOptions> tmdbOptions,
        CancellationToken cancellationToken = default)
    {
        if (query.ContentType == ContentType.Undefined || !Enum.IsDefined(query.ContentType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(StreamTmdbMappingListQuery.ContentType)] = ["The content type is not valid."]
            });
        }

        var page = await streamTmdbMappingAdminService.GetPageAsync(sourceId, query, cancellationToken);
        if (page is null)
        {
            return TypedResults.NotFound();
        }

        var imageBaseUrl = tmdbOptions.Value.ImageBaseUrl;

        return TypedResults.Ok(new AdminStreamTmdbMappingPageDto(
            [.. page.Items.Select(entry => AdminStreamTmdbMappingDto.From(entry, imageBaseUrl))],
            page.MatchingCount,
            page.TotalCount,
            page.MappedCount));
    }

    public static async Task<IResult> PatchAsync(int id, AdminStreamTmdbMappingPatchRequest request, StreamTmdbMappingAdminService streamTmdbMappingAdminService, CancellationToken cancellationToken = default)
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
