using System.Text.Json.Nodes;
using XtreamForge.ApiService.Services.Catalog;
using XtreamForge.ApiService.Services.TmdbInfos;
using XtreamForge.ApiService.Services.WatchHistory;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Xtream;

/// <summary>
/// Handles <c>get_vod_info</c> / <c>get_series_info</c>: the upstream payload is returned with the same category, TMDB ID, and TMDB metadata rewriting
/// as the item lists, or as an empty Xtream payload when the item would not be listed. The item keeps its provider category: only one stream
/// of a TMDB ID is listed in a virtual category, which a single item cannot know.
/// The episodes listed by <c>get_series_info</c> are enqueued to <see cref="SeriesEpisodeQueue"/>, so that their playbacks can be recorded.
/// </summary>
public class ItemGetEndpoint(
    IHttpClientFactory httpClientFactory,
    SourceService sourceService,
    CategoryService categoryService,
    ItemService itemService,
    TmdbInfoService tmdbInfoService,
    SeriesEpisodeQueue seriesEpisodeQueue,
    ILogger<ItemGetEndpoint> logger)
{
    public async Task<IResult> GetAsync(XtreamContext xtreamContext, CancellationToken cancellationToken)
    {
        if (xtreamContext.Action != RequestAction.GetInfo)
        {
            return Results.BadRequest("Invalid request action.");
        }

        var idParameter = xtreamContext.ContentType == ContentType.Vod ? "vod_id" : "series_id";
        var streamId = xtreamContext.Request.Query[idParameter].ToString();
        if (string.IsNullOrWhiteSpace(streamId))
        {
            return Results.BadRequest($"Missing {idParameter}.");
        }

        try
        {
            var source = await sourceService.GetItemSnapshotAsync(xtreamContext, streamId, cancellationToken);
            if (source is null)
            {
                return Results.BadRequest("Unknown source: request the categories first.");
            }

            var xtreamCategoryIdMapping = await categoryService.GetXtreamCategoryIdMappingAsync(xtreamContext, source, cancellationToken);
            if (xtreamCategoryIdMapping.Count == 0)
            {
                await xtreamContext.Response.WriteAsJsonAsync(CreateEmptyInfo(xtreamContext.ContentType), cancellationToken);
                return Results.Empty;
            }

            using var responseMessage = await XtreamHttpForwarder.SendAsync(httpClientFactory, xtreamContext, xtreamContext.BuildTargetUri(), cancellationToken);

            if (!responseMessage.IsSuccessStatusCode)
            {
                await XtreamHttpForwarder.WriteResponseAsync(responseMessage, xtreamContext.Response, cancellationToken);
                return Results.Empty;
            }

            // the payload of a single item is small and must be rewritten, so it is buffered
            await using var payloadStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonNode.ParseAsync(payloadStream, cancellationToken: cancellationToken);

            // the stream URL of an episode only carries its ID: the series of each episode is remembered, whatever the rules decide,
            // as a client may keep the episode list and play an episode later
            if (xtreamContext.ContentType == ContentType.Series && payload is JsonObject seriesInfo)
                seriesEpisodeQueue.TryEnqueue(new SeriesEpisodeRequest(source.Id, streamId, SeriesEpisodeService.ReadEpisodes(seriesInfo)));

            var transformedPayload = ItemService.TransformInfo(payload, streamId, xtreamContext.ContentType, source, xtreamCategoryIdMapping);
            if (transformedPayload is not null)
            {
                var tmdbId = ItemService.ReadInfoTmdbId(transformedPayload, xtreamContext.ContentType);
                var tmdbInfos = await tmdbInfoService.GetAsync(xtreamContext.ContentType, tmdbId is null ? [] : [tmdbId.Value], cancellationToken);

                if (!itemService.EnrichInfo(transformedPayload, xtreamContext.ContentType, tmdbId, tmdbInfos, source))
                    transformedPayload = null;
            }

            await xtreamContext.Response.WriteAsJsonAsync(transformedPayload ?? CreateEmptyInfo(xtreamContext.ContentType), cancellationToken);

            return Results.Empty;
        }
        catch (Exception exception) when (XtreamUpstreamFailure.IsUpstreamFailure(exception, cancellationToken))
        {
            return XtreamUpstreamFailure.Handle(exception, xtreamContext, logger);
        }
    }

    // the payload returned by Xtream panels for an unknown item
    private static JsonObject CreateEmptyInfo(ContentType contentType) => contentType == ContentType.Vod
        ? new JsonObject { ["info"] = new JsonArray(), ["movie_data"] = new JsonArray() }
        : new JsonObject { ["seasons"] = new JsonArray(), ["info"] = new JsonArray(), ["episodes"] = new JsonArray() };
}
