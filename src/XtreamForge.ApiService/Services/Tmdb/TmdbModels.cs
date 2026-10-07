using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb;

/// <summary>
/// Item described by the Xtream provider (get_vod_info / get_series_info) that must be matched with TMDB.
/// Cast and genres are normalized with <see cref="TmdbText.Normalize"/>.
/// </summary>
public sealed class TmdbSourceItem
{
    public ContentType Type { get; init; }
    public string RawTitle { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string PosterId { get; init; } = string.Empty;
    public DateTime? ReleaseDate { get; init; }
    public IReadOnlySet<string> Cast { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> Genres { get; init; } = new HashSet<string>();
    public IReadOnlyList<TmdbSourceSeason> Seasons { get; init; } = [];
    public IReadOnlyList<TmdbSourceEpisode> Episodes { get; init; } = [];

    // without a title nothing can be searched, and a title alone is too weak to avoid false positives
    public bool IsScorable =>
        !string.IsNullOrWhiteSpace(Title)
        && (ReleaseDate is not null || !string.IsNullOrWhiteSpace(PosterId) || Genres.Count > 0 || Cast.Count > 0);
}

public sealed record TmdbSourceSeason(int SeasonNumber, int EpisodeCount);

public sealed record TmdbSourceEpisode(int SeasonNumber, int EpisodeNumber, DateTime? AirDate);

/// <summary>
/// TMDB movie or TV show details. Cast and genres are normalized with <see cref="TmdbText.Normalize"/>.
/// </summary>
public sealed class TmdbCandidate
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string OriginalTitle { get; init; } = string.Empty;
    public DateTime? ReleaseDate { get; init; }
    public string? PosterPath { get; init; }
    public IReadOnlySet<string> Cast { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> Genres { get; init; } = new HashSet<string>();
}

public sealed record TmdbEpisode(int EpisodeNumber, DateTime? AirDate);

/// <summary>
/// Movie or TV show recommended by TMDB for another one of the same kind; any value may be missing. <see cref="ReleaseDate"/> is the first air date
/// of a TV show, and <see cref="Genres"/> are the known English genre names.
/// </summary>
public sealed record TmdbRecommendation(
    long Id,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterPath,
    double? VoteAverage,
    int? VoteCount,
    IReadOnlyList<string> Genres);

/// <summary>
/// TMDB metadata of a movie or TV show as returned by a details request; any value may be missing.
/// <see cref="GenreIds"/> are the stable TMDB genre identifiers; <see cref="Directors"/> are the creators for a TV show,
/// <see cref="Cast"/> is in credit order, and <see cref="DurationMinutes"/> is the movie runtime or the TV episode run time.
/// </summary>
public sealed record TmdbInfoData(
    long Id,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterPath,
    string? Overview,
    double? VoteAverage,
    int? VoteCount,
    IReadOnlyList<int> GenreIds,
    IReadOnlyList<string> Directors,
    IReadOnlyList<string> Cast,
    int? DurationMinutes);
