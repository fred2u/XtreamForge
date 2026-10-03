using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings;

public class StreamTmdbMappingsGetEndpoint(StreamTmdbMappingAdminService streamTmdbMappingAdminService, IOptions<TmdbOptions> tmdbOptions)
{
    public async Task<IResult> GetAsync(int sourceId, StreamTmdbMappingListQuery query, CancellationToken cancellationToken = default)
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
}
