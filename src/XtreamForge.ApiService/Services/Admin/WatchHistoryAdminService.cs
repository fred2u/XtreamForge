using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Admin;

/// <summary>Filters of the watch history list; a null content type keeps every entry.</summary>
public sealed record WatchHistoryListQuery(
    ContentType? ContentType = null,
    int Skip = 0,
    int Take = WatchHistoryAdminService.DefaultPageSize);

/// <summary>A playback of the watch history with the TMDB metadata of its TMDB ID, when loaded.</summary>
public sealed record WatchHistoryEntryItem(
    int Id,
    ContentType ContentType,
    long TmdbId,
    DateTimeOffset StartedAtUtc,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterPath);

/// <summary>A page of the watch history; <see cref="MatchingCount"/> counts the entries matching the filters.</summary>
public sealed record WatchHistoryPage(IReadOnlyList<WatchHistoryEntryItem> Items, int MatchingCount);

public class WatchHistoryAdminService(XtreamForgeDbContext dbContext)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    /// <summary>
    /// Returns a page of the watch history, the most recent playback first: the playbacks are recorded in their start order,
    /// so the most recent one has the highest ID.
    /// </summary>
    public async Task<WatchHistoryPage> GetPageAsync(WatchHistoryListQuery query, CancellationToken cancellationToken = default)
    {
        var entries = dbContext.WatchHistory.AsNoTracking();

        if (query.ContentType is { } contentType)
        {
            entries = entries.Where(entry => entry.ContentType == contentType);
        }

        var matchingCount = await entries.CountAsync(cancellationToken);

        // TMDB metadata is identified by content type and TMDB ID
        var items = await entries
            .OrderByDescending(entry => entry.Id)
            .Skip(Math.Max(query.Skip, 0))
            .Take(Math.Clamp(query.Take, 1, MaximumPageSize))
            .LeftJoin(
                dbContext.TmdbInfos.AsNoTracking(),
                entry => new { entry.ContentType, entry.TmdbId },
                info => new { info.ContentType, info.TmdbId },
                (entry, info) => new WatchHistoryEntryItem(
                    entry.Id,
                    entry.ContentType,
                    entry.TmdbId,
                    entry.StartedAtUtc,
                    info == null ? null : info.Title,
                    info == null ? null : info.OriginalTitle,
                    info == null ? null : info.ReleaseDate,
                    info == null ? null : info.PosterPath))
            .ToListAsync(cancellationToken);

        // the join does not keep the order of the page
        return new WatchHistoryPage([.. items.OrderByDescending(item => item.Id)], matchingCount);
    }
}
