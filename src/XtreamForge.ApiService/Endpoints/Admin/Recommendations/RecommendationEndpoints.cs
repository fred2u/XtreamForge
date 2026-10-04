using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.Recommendations.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;

namespace XtreamForge.ApiService.Endpoints.Admin.Recommendations;

public static class RecommendationEndpoints
{
    public static async Task<IResult> GetAsync(RecommendationService recommendationService, IOptions<TmdbOptions> tmdbOptions, CancellationToken cancellationToken = default)
    {
        var recommendations = await recommendationService.GetAsync(cancellationToken);
        var imageBaseUrl = tmdbOptions.Value.ImageBaseUrl;

        return TypedResults.Ok<IReadOnlyList<AdminRecommendationDto>>(
            [.. recommendations.Select(item => AdminRecommendationDto.From(item, imageBaseUrl))]);
    }
}
