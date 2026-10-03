using XtreamForge.Domain.Enums;

namespace XtreamForge.Domain.Tmdb;

/// <summary>
/// TMDB metadata of a movie or TV show used to enrich the Xtream items, identified by <see cref="ContentType"/> and <see cref="TmdbId"/>
/// (movie and TV IDs overlap). Every value is optional because TMDB may not provide it.
/// </summary>
public sealed class TmdbInfo
{
    public int Id { get; set; }

    public long TmdbId { get; set; }

    public ContentType ContentType { get; set; }

    public string? Title { get; set; }

    public string? OriginalTitle { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    /// <summary>TMDB image path, for example "/abc.jpg"; the URL is built with the configured image base URL and size.</summary>
    public string? PosterPath { get; set; }

    public string? Overview { get; set; }

    public double? VoteAverage { get; set; }

    public int? VoteCount { get; set; }

    /// <summary>Stable TMDB genre identifiers.</summary>
    public List<int> GenreIds { get; set; } = [];

    /// <summary>English names of the known <see cref="GenreIds"/>; unknown identifiers have no name.</summary>
    public List<string> Genres { get; set; } = [];

    /// <summary>Movie directors, or creators of a TV show; only filled by a TMDB details load.</summary>
    public List<string> Directors { get; set; } = [];

    /// <summary>Main cast in TMDB credit order; only filled by a TMDB details load.</summary>
    public List<string> Cast { get; set; } = [];

    /// <summary>Movie runtime or TV episode run time, in minutes; only filled by a TMDB details load.</summary>
    public int? DurationMinutes { get; set; }

    /// <summary>Excluded manually in the admin portal: the movie or TV show is never returned, whatever the source.</summary>
    public bool IsExcluded { get; set; }

    /// <summary>Null until the metadata has been loaded once.</summary>
    public DateTimeOffset? LoadedAtUtc { get; set; }

    /// <summary>Number of loads that ended without metadata (unknown TMDB ID or failure) since the last successful load.</summary>
    public int LoadAttemptCount { get; set; }

    /// <summary>Date from which the metadata is loaded again: refresh of loaded metadata, or retry after a failed load.</summary>
    public DateTimeOffset NextLoadAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
