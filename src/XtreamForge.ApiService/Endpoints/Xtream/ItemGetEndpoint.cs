using System.Text.Json;
using System.Text.Json.Nodes;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Endpoints.Xtream;

/// <summary>
/// Handles <c>get_vod_info</c> / <c>get_series_info</c>: the upstream payload is returned with the same category, TMDB ID, and TMDB metadata rewriting
/// as the item lists, or as an empty Xtream payload when the item would not be listed.
/// </summary>
public class ItemGetEndpoint(
    IHttpClientFactory httpClientFactory,
    SourceService sourceService,
    CategoryService categoryService,
    ItemService itemService,
    TmdbInfoService tmdbInfoService,
    VirtualCategoryService virtualCategoryService,
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
            var source = await sourceService.GetSnapshotAsync(xtreamContext, cancellationToken);
            if (source is null)
            {
                return Results.BadRequest("Unknown source: request the categories first.");
            }

            var xtreamCategoryIdMapping = await categoryService.GetXtreamCategoryIdMappingAsync(xtreamContext, source, cancellationToken);
            if (xtreamCategoryIdMapping.Count == 0)
            {
                await XtreamHttpResponseMessageWriter.WriteAsJsonAsync(CreateEmptyInfo(xtreamContext.ContentType), xtreamContext.Response, cancellationToken);
                return Results.Empty;
            }

            using var requestMessage = XtreamHttpRequestMessageFactory.Create(xtreamContext.BuildTargetUri(), xtreamContext.Request);
            var httpClient = httpClientFactory.CreateClient(XtreamProxyOptions.HttpClientName);
            using var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!responseMessage.IsSuccessStatusCode)
            {
                await XtreamHttpResponseMessageWriter.WriteResponseAsync(responseMessage, xtreamContext.Response, xtreamContext.Request.Method, cancellationToken);
                return Results.Empty;
            }

            // the payload of a single item is small and must be rewritten, so it is buffered
            await using var payloadStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonNode.ParseAsync(payloadStream, cancellationToken: cancellationToken);

            var transformedPayload = ItemService.TransformInfo(payload, streamId, xtreamContext.ContentType, source, xtreamCategoryIdMapping);
            if (transformedPayload is not null)
            {
                var tmdbId = ItemService.ReadInfoTmdbId(transformedPayload, xtreamContext.ContentType);
                var tmdbInfos = await tmdbInfoService.GetAsync(xtreamContext.ContentType, tmdbId is null ? [] : [tmdbId.Value], cancellationToken);

                if (!itemService.EnrichInfo(transformedPayload, xtreamContext.ContentType, tmdbId, tmdbInfos, source))
                    transformedPayload = null;
                else
                    await ApplyVirtualCategoryAsync(transformedPayload, xtreamContext.ContentType, tmdbId, cancellationToken);
            }

            await XtreamHttpResponseMessageWriter.WriteAsJsonAsync(transformedPayload ?? CreateEmptyInfo(xtreamContext.ContentType), xtreamContext.Response, cancellationToken);

            return Results.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(exception, "ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    // an item is in its virtual category (recommendations, popular), as in the list of all the categories
    private async Task ApplyVirtualCategoryAsync(JsonObject payload, ContentType contentType, long? tmdbId, CancellationToken cancellationToken)
    {
        if (await virtualCategoryService.GetItemAssignmentAsync(contentType, cancellationToken) is not { } virtualCategories)
            return;

        // the categories are read from movie_data and info for VOD, from info for series
        JsonObject[] sections = [.. new[] { payload["movie_data"], payload["info"] }.OfType<JsonObject>()];
        ItemService.ApplyVirtualCategory(virtualCategories, tmdbId, sections);
    }

    // the payload returned by Xtream panels for an unknown item
    private static JsonObject CreateEmptyInfo(ContentType contentType) => contentType == ContentType.Vod
        ? new JsonObject { ["info"] = new JsonArray(), ["movie_data"] = new JsonArray() }
        : new JsonObject { ["seasons"] = new JsonArray(), ["info"] = new JsonArray(), ["episodes"] = new JsonArray() };
}
