using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.Queues;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Items;

namespace XtreamForge.ApiService.Services.WatchHistory;

public class SeriesEpisodeService(XtreamForgeDbContext dbContext, TimeProvider timeProvider) : IQueueProcessor<SeriesEpisodeRequest>
{
    Task<bool> IQueueProcessor<SeriesEpisodeRequest>.ProcessAsync(SeriesEpisodeRequest request, CancellationToken cancellationToken)
        => SaveAsync(request, cancellationToken);

    /// <summary>
    /// Reads the episodes of a <c>get_series_info</c> payload. Its <c>episodes</c> section groups them by season: an object keyed by
    /// season number, or an array of seasons for some providers. The season of an episode is its <c>season</c> value, or else the key
    /// of its group; an entry whose <c>id</c> is not a stream ID is ignored, as is a second entry with the same ID.
    /// </summary>
    public static IReadOnlyList<XtreamEpisode> ReadEpisodes(JsonObject seriesInfo)
    {
        ArgumentNullException.ThrowIfNull(seriesInfo);

        IEnumerable<(string? SeasonKey, JsonNode? Episodes)> seasons = seriesInfo["episodes"] switch
        {
            JsonObject bySeason => bySeason.Select(season => ((string?)season.Key, season.Value)),
            JsonArray seasonList => seasonList.Select(season => ((string?)null, season)),
            _ => []
        };

        var episodes = new Dictionary<string, XtreamEpisode>(StringComparer.Ordinal);
        foreach (var (seasonKey, seasonEpisodes) in seasons)
        {
            if (seasonEpisodes is not JsonArray episodeList)
                continue;

            foreach (var episode in episodeList.OfType<JsonObject>())
            {
                var episodeId = episode["id"]?.ToString();
                if (episodeId is null || !XtreamStreamPath.IsStreamId(episodeId))
                    continue;

                episodes.TryAdd(episodeId, new XtreamEpisode(
                    episodeId,
                    ReadNumber(episode["season"]?.ToString()) ?? ReadNumber(seasonKey),
                    ReadNumber(episode["episode_num"]?.ToString())));
            }
        }

        return [.. episodes.Values];
    }

    /// <summary>
    /// Persists the episodes of a series: a new episode is added, a known one is updated when its series, season, or number changed.
    /// Returns true when an episode was added or updated.
    /// </summary>
    public async Task<bool> SaveAsync(SeriesEpisodeRequest request, CancellationToken cancellationToken)
    {
        var episodeIds = request.Episodes.Select(episode => episode.EpisodeId).ToList();
        var knownEpisodes = await dbContext.SeriesEpisodes
            .Where(episode => episode.XtreamSourceId == request.XtreamSourceId && episodeIds.Contains(episode.EpisodeId))
            .ToDictionaryAsync(episode => episode.EpisodeId, StringComparer.Ordinal, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var hasChanges = false;
        foreach (var episode in request.Episodes)
        {
            if (!knownEpisodes.TryGetValue(episode.EpisodeId, out var seriesEpisode))
            {
                dbContext.SeriesEpisodes.Add(new SeriesEpisode
                {
                    XtreamSourceId = request.XtreamSourceId,
                    EpisodeId = episode.EpisodeId,
                    SeriesId = request.SeriesId,
                    SeasonNumber = episode.SeasonNumber,
                    EpisodeNumber = episode.EpisodeNumber,
                    UpdatedAtUtc = now
                });
                hasChanges = true;
            }
            else if (seriesEpisode.SeriesId != request.SeriesId
                || seriesEpisode.SeasonNumber != episode.SeasonNumber
                || seriesEpisode.EpisodeNumber != episode.EpisodeNumber)
            {
                seriesEpisode.SeriesId = request.SeriesId;
                seriesEpisode.SeasonNumber = episode.SeasonNumber;
                seriesEpisode.EpisodeNumber = episode.EpisodeNumber;
                seriesEpisode.UpdatedAtUtc = now;
                hasChanges = true;
            }
        }

        if (!hasChanges)
            return false;

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // a season or episode number is a non-negative number, sent as a number or a string
    private static int? ReadNumber(string? value)
        => int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
}
