using System.Globalization;
using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Web.Features.Tmdb;

// Numeric values must match the ApiService enums: they are exchanged as numbers in JSON.
public enum TmdbExclusionReason
{
    ManuallyExcluded = 1,
    Rule = 2
}

public enum LoadStateFilter
{
    All,
    Loaded,
    NotLoaded
}

public enum MappingStateFilter
{
    All,
    Mapped,
    NotMapped
}

/// <summary>
/// TMDB metadata entry as listed by the API, with the decision of the manual exclusion and the TMDB rules.
/// <see cref="DecidingRule"/> is null when the entry is excluded manually (rules are not evaluated) or no rule matched.
/// </summary>
public sealed record TmdbInfoDto(
    int Id,
    long TmdbId,
    ContentType ContentType,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterThumbnailUrl,
    string? PosterUrl,
    IReadOnlyList<string> Genres,
    double? VoteAverage,
    int? VoteCount,
    bool IsExcluded,
    DateTimeOffset? LoadedAtUtc,
    int LoadAttemptCount,
    DateTimeOffset NextLoadAtUtc,
    InclusionDecision Decision,
    TmdbExclusionReason? ExclusionReason,
    RuleDto? DecidingRule)
{
    public bool IsLoaded => LoadedAtUtc is not null;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? $"TMDB #{TmdbId.ToString(CultureInfo.InvariantCulture)}" : Title;

    /// <summary>Page of the movie or TV show on the TMDB website.</summary>
    public string TmdbUrl => TmdbLinks.Page(ContentType, TmdbId);
}

/// <summary>Every value of a TMDB metadata entry, shown in its details.</summary>
public sealed record TmdbInfoDetailsDto(
    TmdbInfoDto Info,
    string? Overview,
    IReadOnlyList<string> Directors,
    IReadOnlyList<string> Cast,
    int? DurationMinutes);

/// <summary>A page of TMDB metadata; the counters and the genres (sorted names) cover every entry of the content type, whatever the filters.</summary>
public sealed record TmdbInfoPageDto(
    IReadOnlyList<TmdbInfoDto> Items,
    int MatchingCount,
    int TotalCount,
    int ExcludedCount,
    int ManuallyExcludedCount,
    int NotLoadedCount,
    IReadOnlyList<string> Genres);

public sealed record TmdbInfoPatchRequest(bool IsExcluded);

/// <summary>Filters of the TMDB infos screen, applied by the API (the list is paged on the server).</summary>
public sealed record TmdbInfoFilter(
    string? Search = null,
    DecisionFilter Decision = DecisionFilter.All,
    ManualExclusionFilter ManualExclusion = ManualExclusionFilter.All,
    LoadStateFilter LoadState = LoadStateFilter.All,
    string? Genre = null)
{
    public bool IsEmpty => this == new TmdbInfoFilter();

    /// <summary>
    /// Filter set by a link to the TMDB infos screen (query parameters <c>decision</c>, <c>manual</c>, <c>load</c>, values are the filter enum names);
    /// null without any filter parameter. A link with at least one filter parameter sets every filter: absent or unknown ones are reset to "all".
    /// </summary>
    public static TmdbInfoFilter? FromLink(string? search, string? decision, string? manualExclusion, string? loadState)
    {
        if (decision is null && manualExclusion is null && loadState is null)
            return null;

        return new TmdbInfoFilter(search, ParseFilter<DecisionFilter>(decision), ParseFilter<ManualExclusionFilter>(manualExclusion), ParseFilter<LoadStateFilter>(loadState));
    }

    private static TEnum ParseFilter<TEnum>(string? value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var filter) && Enum.IsDefined(filter) ? filter : default;

    /// <summary>Query string of <c>GET /api/admin/tmdb-infos</c> for this filter and page.</summary>
    public string ToQueryString(ContentType contentType, int skip, int take)
    {
        var parameters = new List<string>
        {
            $"contentType={contentType}",
            $"skip={skip.ToString(CultureInfo.InvariantCulture)}",
            $"take={take.ToString(CultureInfo.InvariantCulture)}"
        };

        if (Search?.Trim() is { Length: > 0 } search)
            parameters.Add($"search={Uri.EscapeDataString(search)}");

        if (Genre?.Trim() is { Length: > 0 } genre)
            parameters.Add($"genre={Uri.EscapeDataString(genre)}");

        if (Decision != DecisionFilter.All)
            parameters.Add($"decision={(Decision == DecisionFilter.Excluded ? InclusionDecision.Exclude : InclusionDecision.Include)}");

        if (ManualExclusion != ManualExclusionFilter.All)
            parameters.Add($"isExcluded={(ManualExclusion == ManualExclusionFilter.Excluded ? "true" : "false")}");

        if (LoadState != LoadStateFilter.All)
            parameters.Add($"isLoaded={(LoadState == LoadStateFilter.Loaded ? "true" : "false")}");

        return string.Join('&', parameters);
    }
}

/// <summary>
/// Stream TMDB mapping of a source as listed by the API. <see cref="TmdbId"/> is null while no TMDB ID has been found;
/// the TMDB values are null when the metadata of the TMDB ID is not loaded.
/// </summary>
public sealed record TmdbMappingDto(
    int Id,
    ContentType ContentType,
    string StreamId,
    long? TmdbId,
    int LookupAttemptCount,
    DateTimeOffset? NextLookupAtUtc,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterThumbnailUrl)
{
    public bool IsMapped => TmdbId is not null;

    /// <summary>Page of the movie or TV show on the TMDB website, or null when not mapped.</summary>
    public string? TmdbUrl => TmdbId is { } tmdbId ? TmdbLinks.Page(ContentType, tmdbId) : null;
}

/// <summary>A page of stream TMDB mappings; the counters cover every mapping of the source and content type, whatever the filters.</summary>
public sealed record TmdbMappingPageDto(
    IReadOnlyList<TmdbMappingDto> Items,
    int MatchingCount,
    int TotalCount,
    int MappedCount);

public sealed record TmdbMappingPatchRequest(long TmdbId);

/// <summary>Filters of the TMDB mappings screen, applied by the API (the list is paged on the server).</summary>
public sealed record TmdbMappingFilter(string? Search = null, MappingStateFilter State = MappingStateFilter.All)
{
    public bool IsEmpty => this == new TmdbMappingFilter();

    /// <summary>Query string of <c>GET /api/admin/sources/{sourceId}/tmdb-mappings</c> for this filter and page.</summary>
    public string ToQueryString(ContentType contentType, int skip, int take)
    {
        var parameters = new List<string>
        {
            $"contentType={contentType}",
            $"skip={skip.ToString(CultureInfo.InvariantCulture)}",
            $"take={take.ToString(CultureInfo.InvariantCulture)}"
        };

        if (Search?.Trim() is { Length: > 0 } search)
            parameters.Add($"search={Uri.EscapeDataString(search)}");

        if (State != MappingStateFilter.All)
            parameters.Add($"isMapped={(State == MappingStateFilter.Mapped ? "true" : "false")}");

        return string.Join('&', parameters);
    }
}

public static class TmdbLinks
{
    /// <summary>Page of a movie or TV show on the TMDB website.</summary>
    public static string Page(ContentType contentType, long tmdbId) =>
        $"https://www.themoviedb.org/{(contentType == ContentType.Series ? "tv" : "movie")}/{tmdbId.ToString(CultureInfo.InvariantCulture)}";
}
