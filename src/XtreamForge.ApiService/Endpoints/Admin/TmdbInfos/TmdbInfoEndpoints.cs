using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbInfos;

public static class TmdbInfoEndpoints
{
    public static async Task<IResult> GetListAsync(
        [AsParameters] TmdbInfoListQuery query,
        TmdbInfoAdminService tmdbInfoAdminService,
        IOptions<TmdbOptions> tmdbOptions,
        CancellationToken cancellationToken = default)
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

    public static async Task<IResult> GetAsync(int id, TmdbInfoAdminService tmdbInfoAdminService, IOptions<TmdbOptions> tmdbOptions, CancellationToken cancellationToken = default)
    {
        var evaluation = await tmdbInfoAdminService.GetAsync(id, cancellationToken);

        return evaluation is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(AdminTmdbInfoDetailsDto.From(evaluation, tmdbOptions.Value.ImageBaseUrl));
    }

    public static async Task<IResult> PatchAsync(int id, AdminTmdbInfoPatchRequest request, TmdbInfoAdminService tmdbInfoAdminService, CancellationToken cancellationToken = default)
    {
        var found = await tmdbInfoAdminService.SetExcludedAsync(id, request.IsExcluded, cancellationToken);

        return found
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
