using System.Globalization;
using XtreamForge.Web.Features.Categories;
using XtreamForge.Web.Features.Tmdb;

namespace XtreamForge.Web.Features.History;

/// <summary>
/// Playback of the watch history as listed by the API; the TMDB values are null when the metadata of the TMDB ID is not loaded.
/// A series episode has the TMDB ID and metadata of its series, and its season and episode numbers when known.
/// The source is null for a playback added from the TMDB infos, or once its source is deleted.
/// </summary>
public sealed record WatchHistoryEntryDto(
    int Id,
    XtreamSourceDto? Source,
    ContentType ContentType,
    long TmdbId,
    int? SeasonNumber,
    int? EpisodeNumber,
    DateTimeOffset StartedAtUtc,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterThumbnailUrl)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? $"TMDB #{TmdbId.ToString(CultureInfo.InvariantCulture)}" : Title;

    /// <summary>Season and number of a series episode (<c>S01E02</c>, or only the known part); null for a movie or when unknown.</summary>
    public string? EpisodeLabel => (SeasonNumber, EpisodeNumber) switch
    {
        ({ } season, { } episode) => $"S{season.ToString("00", CultureInfo.InvariantCulture)}E{episode.ToString("00", CultureInfo.InvariantCulture)}",
        ({ } season, null) => $"S{season.ToString("00", CultureInfo.InvariantCulture)}",
        (null, { } episode) => $"E{episode.ToString("00", CultureInfo.InvariantCulture)}",
        _ => null
    };

    /// <summary>Episode, TMDB ID, release year, and original title when it differs from the title.</summary>
    public string Subtitle
    {
        get
        {
            List<string> parts = [];
            if (EpisodeLabel is { } episodeLabel)
            {
                parts.Add(episodeLabel);
            }

            parts.Add($"TMDB #{TmdbId.ToString(CultureInfo.InvariantCulture)}");
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

    /// <summary>Title with the episode, for the messages about this playback.</summary>
    public string FullTitle => EpisodeLabel is { } episodeLabel ? $"{DisplayTitle} {episodeLabel}" : DisplayTitle;

    /// <summary>Page of the movie or TV show on the TMDB website.</summary>
    public string TmdbUrl => TmdbLinks.Page(ContentType, TmdbId);
}

/// <summary>A page of the watch history; <see cref="MatchingCount"/> counts the entries matching the content type filter.</summary>
public sealed record WatchHistoryPageDto(IReadOnlyList<WatchHistoryEntryDto> Items, int MatchingCount);
