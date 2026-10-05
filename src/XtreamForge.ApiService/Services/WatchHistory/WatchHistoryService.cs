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
    /// Records a movie playback with its TMDB ID; returns false when the source is unknown or the movie has no TMDB ID.
    /// The stored TMDB mapping wins, as in the item lists; otherwise the provider <c>get_vod_info</c> payload is read,
    /// since the movies already identified by the provider have no mapping.
    /// </summary>
    public async Task<bool> RecordAsync(WatchHistoryRequest request, CancellationToken cancellationToken)
    {
        var sourceId = await dbContext.XtreamSources
            .Where(source => source.Protocol == request.Protocol && source.Host == request.Host && source.Port == request.Port)
            .Select(source => (int?)source.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (sourceId is null)
            return false;

        var tmdbId = await dbContext.StreamTmdbMappings
            .Where(mapping => mapping.XtreamSourceId == sourceId && mapping.ContentType == ContentType.Vod && mapping.StreamId == request.Movie.StreamId)
            .Select(mapping => mapping.TmdbId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? await GetProviderTmdbIdAsync(request, cancellationToken);

        if (tmdbId is null)
            return false;

        dbContext.WatchHistory.Add(new WatchHistoryEntry
        {
            ContentType = ContentType.Vod,
            TmdbId = tmdbId.Value,
            StartedAtUtc = request.StartedAtUtc
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        recommendationCache.Invalidate(TmdbIdCache.RecommendationsKey);

        return true;
    }

    private async Task<long?> GetProviderTmdbIdAsync(WatchHistoryRequest request, CancellationToken cancellationToken)
    {
        var movie = request.Movie;
        var uriBuilder = new UriBuilder(request.Protocol, request.Host, request.Port, "/player_api.php")
        {
            Query = $"username={Uri.EscapeDataString(movie.Username)}&password={Uri.EscapeDataString(movie.Password)}&action=get_vod_info&vod_id={Uri.EscapeDataString(movie.StreamId)}"
        };

        var httpClient = httpClientFactory.CreateClient(XtreamProxyOptions.HttpClientName);

        using var responseMessage = await httpClient.GetAsync(uriBuilder.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        responseMessage.EnsureSuccessStatusCode();

        await using var stream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);

        return payload is JsonObject info ? ItemService.ReadInfoTmdbId(info, ContentType.Vod) : null;
    }
}
