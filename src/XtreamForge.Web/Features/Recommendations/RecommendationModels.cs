using System.Globalization;
using XtreamForge.Web.Features.Categories;
using XtreamForge.Web.Features.Tmdb;

namespace XtreamForge.Web.Features.Recommendations;

/// <summary>Movie recommended from the watch history; <see cref="IsInCatalogue"/> tells whether a source exposes it.</summary>
public sealed record RecommendationDto(
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
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? $"TMDB #{TmdbId.ToString(CultureInfo.InvariantCulture)}" : Title;

    /// <summary>Release year, TMDB ID (unless already displayed as the title), and original title when it differs from the title.</summary>
    public string Subtitle
    {
        get
        {
            List<string> parts = [];
            if (ReleaseDate is { } releaseDate)
            {
                parts.Add(releaseDate.Year.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(Title))
            {
                parts.Add($"TMDB #{TmdbId.ToString(CultureInfo.InvariantCulture)}");
            }

            if (!string.IsNullOrWhiteSpace(OriginalTitle) && OriginalTitle != Title)
            {
                parts.Add(OriginalTitle);
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Page of the movie on the TMDB website.</summary>
    public string TmdbUrl => TmdbLinks.Page(ContentType.Vod, TmdbId);
}
