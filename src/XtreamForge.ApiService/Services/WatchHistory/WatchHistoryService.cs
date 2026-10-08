using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Catalog;
using XtreamForge.ApiService.Services.Queues;
using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.History;

namespace XtreamForge.ApiService.Services.WatchHistory;

public class WatchHistoryService(IHttpClientFactory httpClientFactory, XtreamForgeDbContext dbContext, TmdbIdCache recommendationCache)
    : IQueueProcessor<WatchHistoryRequest>
{
    Task<bool> IQueueProcessor<WatchHistoryRequest>.ProcessAsync(WatchHistoryRequest request, CancellationToken cancellationToken)
        => RecordAsync(request, cancellationToken);

    /// <summary>
    /// Records a movie or series episode playback with its source and TMDB ID (the TMDB ID of the series for an episode); returns false when
    /// the source is unknown, the episode was not listed by <c>get_series_info</c>, or the movie or series has no TMDB ID.
    /// The stored TMDB mapping wins, as in the item lists; otherwise the provider <c>get_vod_info</c> / <c>get_series_info</c> payload is read,
    /// since the items already identified by the provider may have no mapping.
    /// </summary>
    public async Task<bool> RecordAsync(WatchHistoryRequest request, CancellationToken cancellationToken)
    {
        var sourceId = await dbContext.XtreamSources
            .Where(source => source.Protocol == request.Protocol && source.Host == request.Host && source.Port == request.Port)
            .Select(source => (int?)source.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (sourceId is null)
            return false;

        var entry = request.Stream.ContentType == ContentType.Series
            ? await CreateEpisodeEntryAsync(request, sourceId.Value, cancellationToken)
            : await CreateMovieEntryAsync(request, sourceId.Value, cancellationToken);

        if (entry is null)
            return false;

        dbContext.WatchHistory.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        recommendationCache.Invalidate(TmdbIdCache.RecommendationsKey(entry.ContentType));

        return true;
    }

    private async Task<WatchHistoryEntry?> CreateMovieEntryAsync(WatchHistoryRequest request, int sourceId, CancellationToken cancellationToken)
    {
        var tmdbId = await GetTmdbIdAsync(request, sourceId, ContentType.Vod, request.Stream.StreamId, cancellationToken);

        return tmdbId is null
            ? null
            : new WatchHistoryEntry { XtreamSourceId = sourceId, ContentType = ContentType.Vod, TmdbId = tmdbId.Value, StartedAtUtc = request.StartedAtUtc };
    }

    // the stream URL of an episode only carries its ID: its series, season, and number come from the episodes listed by get_series_info
    private async Task<WatchHistoryEntry?> CreateEpisodeEntryAsync(WatchHistoryRequest request, int sourceId, CancellationToken cancellationToken)
    {
        var episode = await dbContext.SeriesEpisodes
            .AsNoTracking()
            .Where(episode => episode.XtreamSourceId == sourceId && episode.EpisodeId == request.Stream.StreamId)
            .Select(episode => new { episode.SeriesId, episode.SeasonNumber, episode.EpisodeNumber })
            .SingleOrDefaultAsync(cancellationToken);

        if (episode is null)
            return null;

        var tmdbId = await GetTmdbIdAsync(request, sourceId, ContentType.Series, episode.SeriesId, cancellationToken);

        return tmdbId is null
            ? null
            : new WatchHistoryEntry
            {
                XtreamSourceId = sourceId,
                ContentType = ContentType.Series,
                TmdbId = tmdbId.Value,
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                StartedAtUtc = request.StartedAtUtc
            };
    }

    private async Task<long?> GetTmdbIdAsync(WatchHistoryRequest request, int sourceId, ContentType contentType, string streamId, CancellationToken cancellationToken)
        => await dbContext.StreamTmdbMappings
            .Where(mapping => mapping.XtreamSourceId == sourceId && mapping.ContentType == contentType && mapping.StreamId == streamId)
            .Select(mapping => mapping.TmdbId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? await GetProviderTmdbIdAsync(request, contentType, streamId, cancellationToken);

    private async Task<long?> GetProviderTmdbIdAsync(WatchHistoryRequest request, ContentType contentType, string streamId, CancellationToken cancellationToken)
    {
        var (action, idParameter) = contentType == ContentType.Vod ? ("get_vod_info", "vod_id") : ("get_series_info", "series_id");
        var stream = request.Stream;
        var uriBuilder = new UriBuilder(request.Protocol, request.Host, request.Port, "/player_api.php")
        {
            Query = $"username={Uri.EscapeDataString(stream.Username)}&password={Uri.EscapeDataString(stream.Password)}&action={action}&{idParameter}={Uri.EscapeDataString(streamId)}"
        };

        var httpClient = httpClientFactory.CreateClient(XtreamProxyOptions.HttpClientName);

        using var responseMessage = await httpClient.GetAsync(uriBuilder.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        responseMessage.EnsureSuccessStatusCode();

        await using var payloadStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonNode.ParseAsync(payloadStream, cancellationToken: cancellationToken);

        return payload is JsonObject info ? ItemService.ReadInfoTmdbId(info, contentType) : null;
    }
}
