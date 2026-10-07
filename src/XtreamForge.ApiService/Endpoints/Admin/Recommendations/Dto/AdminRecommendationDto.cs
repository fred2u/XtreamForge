using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos.Dto;
using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.Recommendations.Dto;

/// <summary>Movie or TV show recommended from the watch history; <see cref="IsInCatalogue"/> tells whether a source exposes it.</summary>
public sealed record AdminRecommendationDto(
    ContentType ContentType,
    long TmdbId,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterThumbnailUrl,
    double? VoteAverage,
    int? VoteCount,
    IReadOnlyList<string> Genres,
    int Score,
    int RecommendedByCount,
    bool IsInCatalogue)
{
    public static AdminRecommendationDto From(RecommendationItem item, string imageBaseUrl) => new(
        item.ContentType,
        item.TmdbId,
        item.Title,
        item.OriginalTitle,
        item.ReleaseDate,
        TmdbItemEnricher.BuildImageUrl(imageBaseUrl, AdminTmdbInfoDto.PosterThumbnailSize, item.PosterPath),
        item.VoteAverage,
        item.VoteCount,
        item.Genres,
        item.Score,
        item.RecommendedByCount,
        item.IsInCatalogue);
}
