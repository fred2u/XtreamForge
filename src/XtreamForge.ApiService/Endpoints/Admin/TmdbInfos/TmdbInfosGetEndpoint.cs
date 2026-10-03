using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbInfos;

public class TmdbInfosGetEndpoint(TmdbInfoAdminService tmdbInfoAdminService, IOptions<TmdbOptions> tmdbOptions)
{
    public async Task<IResult> GetAsync(TmdbInfoListQuery query, CancellationToken cancellationToken = default)
    {
        if (query.ContentType == ContentType.Undefined || !Enum.IsDefined(query.ContentType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(TmdbInfoListQuery.ContentType)] = ["The content type is not valid."]
            });
        }

        var page = await tmdbInfoAdminService.GetPageAsync(query, cancellationToken);
        var imageBaseUrl = tmdbOptions.Value.ImageBaseUrl;

        return TypedResults.Ok(new AdminTmdbInfoPageDto(
            [.. page.Items.Select(evaluation => AdminTmdbInfoDto.From(evaluation, imageBaseUrl))],
            page.MatchingCount,
            page.TotalCount,
            page.ExcludedCount,
            page.ManuallyExcludedCount,
            page.NotLoadedCount,
            page.Genres));
    }
}
