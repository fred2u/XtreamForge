using XtreamForge.ApiService.Endpoints.Admin.TmdbRules.Dto;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Endpoints.Admin.TmdbInfos.Dto;

/// <summary>
/// TMDB metadata entry as listed by the admin portal, with the decision of the manual exclusion and the TMDB rules.
/// <see cref="DecidingRule"/> is null when the entry is excluded manually (rules are not evaluated) or no rule matched.
/// </summary>
public sealed record AdminTmdbInfoDto(
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
    AdminTmdbRuleDto? DecidingRule)
{
    public const string PosterThumbnailSize = "w92";

    public static AdminTmdbInfoDto From(TmdbRuleEvaluation evaluation, string imageBaseUrl)
    {
        var info = evaluation.Info;

        return new AdminTmdbInfoDto(
            info.Id,
            info.TmdbId,
            info.ContentType,
            info.Title,
            info.OriginalTitle,
            info.ReleaseDate,
            TmdbItemEnricher.BuildImageUrl(imageBaseUrl, PosterThumbnailSize, info.PosterPath),
            TmdbItemEnricher.BuildImageUrl(imageBaseUrl, TmdbItemEnricher.MediumPosterSize, info.PosterPath),
            info.Genres,
            info.VoteAverage,
            info.VoteCount,
            info.IsExcluded,
            info.LoadedAtUtc,
            info.LoadAttemptCount,
            info.NextLoadAtUtc,
            evaluation.Decision,
            evaluation.ExclusionReason,
            evaluation.DecidingRule is { } rule ? AdminTmdbRuleDto.FromRule(rule) : null);
    }
}

/// <summary>Every value of a TMDB metadata entry, shown in its details.</summary>
public sealed record AdminTmdbInfoDetailsDto(
    AdminTmdbInfoDto Info,
    string? Overview,
    IReadOnlyList<string> Directors,
    IReadOnlyList<string> Cast,
    int? DurationMinutes)
{
    public static AdminTmdbInfoDetailsDto From(TmdbRuleEvaluation evaluation, string imageBaseUrl) => new(
        AdminTmdbInfoDto.From(evaluation, imageBaseUrl),
        evaluation.Info.Overview,
        evaluation.Info.Directors,
        evaluation.Info.Cast,
        evaluation.Info.DurationMinutes);
}

/// <summary>A page of TMDB metadata; the counters and the genres cover every entry of the content type, whatever the filters.</summary>
public sealed record AdminTmdbInfoPageDto(
    IReadOnlyList<AdminTmdbInfoDto> Items,
    int MatchingCount,
    int TotalCount,
    int ExcludedCount,
    int ManuallyExcludedCount,
    int NotLoadedCount,
    IReadOnlyList<string> Genres);

public sealed record AdminTmdbInfoPatchRequest(bool IsExcluded);
