using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings.Dto;

/// <summary>
/// Stream TMDB mapping as listed by the admin portal. <see cref="TmdbId"/> is null while no TMDB ID has been found;
/// the TMDB values are null when the metadata of the TMDB ID is not loaded.
/// </summary>
public sealed record AdminStreamTmdbMappingDto(
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
    public static AdminStreamTmdbMappingDto From(StreamTmdbMappingEntry entry, string imageBaseUrl) => new(
        entry.Id,
        entry.ContentType,
        entry.StreamId,
        entry.TmdbId,
        entry.LookupAttemptCount,
        entry.NextLookupAtUtc,
        entry.Title,
        entry.OriginalTitle,
        entry.ReleaseDate,
        TmdbItemEnricher.BuildImageUrl(imageBaseUrl, AdminTmdbInfoDto.PosterThumbnailSize, entry.PosterPath));
}

/// <summary>A page of stream TMDB mappings; the counters cover every mapping of the source and content type, whatever the filters.</summary>
public sealed record AdminStreamTmdbMappingPageDto(
    IReadOnlyList<AdminStreamTmdbMappingDto> Items,
    int MatchingCount,
    int TotalCount,
    int MappedCount);

public sealed record AdminStreamTmdbMappingPatchRequest(long TmdbId);
