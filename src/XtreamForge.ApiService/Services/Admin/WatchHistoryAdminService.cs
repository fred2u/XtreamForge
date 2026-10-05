using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.History;

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

/// <summary>Number of movie playbacks started on a day of the requested time zone.</summary>
public sealed record WatchHistoryDayActivity(DateOnly Date, int Count);

/// <summary>Movie playbacks per day from <see cref="From"/> to <see cref="To"/> (included); the days without playback are omitted.</summary>
public sealed record WatchHistoryActivity(DateOnly From, DateOnly To, IReadOnlyList<WatchHistoryDayActivity> Days);

public class WatchHistoryAdminService(XtreamForgeDbContext dbContext, TmdbIdCache recommendationCache, TimeProvider timeProvider)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    /// <summary>Number of days of the activity, today included: 53 full weeks.</summary>
    public const int ActivityDayCount = 371;

    /// <summary>
    /// Counts the movie playbacks per day of <paramref name="timeZone"/> over the last <see cref="ActivityDayCount"/> days, today included.
    /// </summary>
    public async Task<WatchHistoryActivity> GetActivityAsync(TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        var to = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);
        var from = to.AddDays(1 - ActivityDayCount);

        // only the playbacks of the period are read: a local day starts at most 14 hours before the same UTC day (UTC+14),
        // so starting one UTC day before the first day keeps all of its playbacks; their start dates are then converted to the
        // time zone in memory, which keeps the day boundaries exact for every offset and daylight saving time
        var fromUtc = new DateTimeOffset(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var startDates = await dbContext.WatchHistory
            .AsNoTracking()
            .Where(entry => entry.ContentType == ContentType.Vod && entry.StartedAtUtc >= fromUtc)
            .Select(entry => entry.StartedAtUtc)
            .ToListAsync(cancellationToken);

        var days = startDates
            .Select(startedAtUtc => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startedAtUtc, timeZone).DateTime))
            .Where(date => date >= from && date <= to)
            .GroupBy(date => date)
            .Select(group => new WatchHistoryDayActivity(group.Key, group.Count()))
            .OrderBy(day => day.Date)
            .ToList();

        return new WatchHistoryActivity(from, to, days);
    }

    /// <summary>
    /// Records a playback of a TMDB metadata entry started now, as if it had been played through the proxy;
    /// returns null when the entry does not exist.
    /// </summary>
    public async Task<WatchHistoryEntryItem?> AddAsync(int tmdbInfoId, CancellationToken cancellationToken = default)
    {
        var info = await dbContext.TmdbInfos.AsNoTracking().FirstOrDefaultAsync(info => info.Id == tmdbInfoId, cancellationToken);
        if (info is null)
        {
            return null;
        }

        var entry = new WatchHistoryEntry { ContentType = info.ContentType, TmdbId = info.TmdbId, StartedAtUtc = timeProvider.GetUtcNow() };
        dbContext.WatchHistory.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        recommendationCache.Invalidate(TmdbIdCache.RecommendationsKey);

        return new WatchHistoryEntryItem(entry.Id, entry.ContentType, entry.TmdbId, entry.StartedAtUtc, info.Title, info.OriginalTitle, info.ReleaseDate, info.PosterPath);
    }

    /// <summary>Deletes one playback of the watch history; returns false when it does not exist.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await dbContext.WatchHistory.Where(entry => entry.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        if (deleted)
        {
            recommendationCache.Invalidate(TmdbIdCache.RecommendationsKey);
        }

        return deleted;
    }

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
