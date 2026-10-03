using System.Globalization;
using XtreamForge.Web.Features.Categories;
using XtreamForge.Web.Features.Tmdb;

namespace XtreamForge.Web.Features.History;

/// <summary>Playback of the watch history as listed by the API; the TMDB values are null when the metadata of the TMDB ID is not loaded.</summary>
public sealed record WatchHistoryEntryDto(
    int Id,
    ContentType ContentType,
    long TmdbId,
    DateTimeOffset StartedAtUtc,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterThumbnailUrl)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? $"TMDB #{TmdbId.ToString(CultureInfo.InvariantCulture)}" : Title;

    /// <summary>TMDB ID, release year, and original title when it differs from the title.</summary>
    public string Subtitle
    {
        get
        {
            List<string> parts = [$"TMDB #{TmdbId.ToString(CultureInfo.InvariantCulture)}"];
            if (ReleaseDate is { } releaseDate)
            {
                parts.Add(releaseDate.Year.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(OriginalTitle) && OriginalTitle != Title)
            {
                parts.Add(OriginalTitle);
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Page of the movie or TV show on the TMDB website.</summary>
    public string TmdbUrl => TmdbLinks.Page(ContentType, TmdbId);
}

/// <summary>A page of the watch history; <see cref="MatchingCount"/> counts every entry.</summary>
public sealed record WatchHistoryPageDto(IReadOnlyList<WatchHistoryEntryDto> Items, int MatchingCount);
