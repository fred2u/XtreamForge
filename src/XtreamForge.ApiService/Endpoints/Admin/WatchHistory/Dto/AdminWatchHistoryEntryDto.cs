using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.WatchHistory.Dto;

/// <summary>Playback of the watch history; the TMDB values are null when the metadata of the TMDB ID is not loaded.</summary>
public sealed record AdminWatchHistoryEntryDto(
    int Id,
    ContentType ContentType,
    long TmdbId,
    DateTimeOffset StartedAtUtc,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterThumbnailUrl)
{
    public static AdminWatchHistoryEntryDto From(WatchHistoryEntryItem item, string imageBaseUrl) => new(
        item.Id,
        item.ContentType,
        item.TmdbId,
        item.StartedAtUtc,
        item.Title,
        item.OriginalTitle,
        item.ReleaseDate,
        TmdbItemEnricher.BuildImageUrl(imageBaseUrl, AdminTmdbInfoDto.PosterThumbnailSize, item.PosterPath));
}

/// <summary>A page of the watch history; <see cref="MatchingCount"/> counts the entries matching the filters.</summary>
public sealed record AdminWatchHistoryPageDto(IReadOnlyList<AdminWatchHistoryEntryDto> Items, int MatchingCount);
