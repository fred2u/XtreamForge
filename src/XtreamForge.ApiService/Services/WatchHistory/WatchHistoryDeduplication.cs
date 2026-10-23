using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;
using XtreamForge.Domain.History;

namespace XtreamForge.ApiService.Services.WatchHistory;

/// <summary>
/// A movie or series episode appears once in the watch history for playbacks close in time: a playback started at most
/// <see cref="Window"/> after the last one of the same movie or episode only moves that entry to the new start date and source.
/// </summary>
public static class WatchHistoryDeduplication
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(36);

    /// <summary>
    /// Adds <paramref name="playback"/> to the history, or updates the entry of the same content type, TMDB ID, season and episode
    /// started at most <see cref="Window"/> before it; returns the added or updated entry. The changes are not saved.
    /// </summary>
    public static async Task<WatchHistoryEntry> AddOrUpdateAsync(XtreamForgeDbContext dbContext, WatchHistoryEntry playback, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(playback);

        var (contentType, tmdbId, seasonNumber, episodeNumber) = (playback.ContentType, playback.TmdbId, playback.SeasonNumber, playback.EpisodeNumber);
        var windowStartUtc = playback.StartedAtUtc - Window;

        // an entry started after the playback is also kept: the playbacks may be recorded slightly out of order
        var recentEntry = await dbContext.WatchHistory
            .Where(entry => entry.ContentType == contentType
                && entry.TmdbId == tmdbId
                && entry.SeasonNumber == seasonNumber
                && entry.EpisodeNumber == episodeNumber
                && entry.StartedAtUtc >= windowStartUtc)
            .OrderByDescending(entry => entry.StartedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentEntry is null)
        {
            dbContext.WatchHistory.Add(playback);
            return playback;
        }

        if (playback.StartedAtUtc > recentEntry.StartedAtUtc)
        {
            recentEntry.StartedAtUtc = playback.StartedAtUtc;
            recentEntry.XtreamSourceId = playback.XtreamSourceId;
        }

        return recentEntry;
    }
}
