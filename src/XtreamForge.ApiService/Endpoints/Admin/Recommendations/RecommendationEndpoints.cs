using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.Recommendations.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.Recommendations;

public static class RecommendationEndpoints
{
    /// <summary>Recommended movies (<paramref name="contentType"/> VOD, the default) or TV shows (series).</summary>
    public static async Task<IResult> GetAsync(
        ContentType? contentType,
        RecommendationService recommendationService,
        IOptions<TmdbOptions> tmdbOptions,
        CancellationToken cancellationToken = default)
    {
        var type = contentType ?? ContentType.Vod;
        if (type == ContentType.Undefined || !Enum.IsDefined(type))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(contentType)] = ["The content type is not valid."]
            });
        }

        var recommendations = await recommendationService.GetAsync(type, cancellationToken);
        var imageBaseUrl = tmdbOptions.Value.ImageBaseUrl;

        return TypedResults.Ok<IReadOnlyList<AdminRecommendationDto>>(
            [.. recommendations.Select(item => AdminRecommendationDto.From(item, imageBaseUrl))]);
    }
}
