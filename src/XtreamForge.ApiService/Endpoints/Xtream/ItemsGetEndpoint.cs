using System.Text.Json;
using System.Text.Json.Nodes;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Endpoints.Xtream;

public class ItemsGetEndpoint(IHttpClientFactory httpClientFactory, SourceService sourceService, CategoryService categoryService, ItemService itemService, TmdbInfoService tmdbInfoService, ILogger<ItemsGetEndpoint> logger)
{
    // the response is flushed in chunks rather than after every item
    private const int FlushThresholdBytes = 32 * 1024;

    // the TMDB metadata is loaded with one query per batch: preloading it for a whole catalogue would hold every overview in memory
    private const int TmdbInfoBatchSize = 500;

    public async Task<IResult> GetAsync(XtreamContext xtreamContext, CancellationToken cancellationToken)
    {
        if (xtreamContext.Action != Domain.Enums.RequestAction.GetItems)
        {
            return Results.BadRequest("Invalid request action.");
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
                return Results.BadRequest("No categories found for the requested category_id.");
            }

            // a single upstream request: the only mapped category, or ALL filtered through the category mapping
            var upstreamCategoryId = !CategoryService.IsGetAll(xtreamContext) && xtreamCategoryIdMapping.Count == 1
                ? xtreamCategoryIdMapping.Keys.Single()
                : "ALL";

            using var requestMessage = XtreamHttpRequestMessageFactory.Create(xtreamContext.BuildTargetUri(new KeyValuePair<string, string>("category_id", upstreamCategoryId)), xtreamContext.Request);
            var httpClient = httpClientFactory.CreateClient(XtreamProxyOptions.HttpClientName);
            using var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!responseMessage.IsSuccessStatusCode)
            {
                await XtreamHttpResponseMessageWriter.WriteResponseAsync(responseMessage, xtreamContext.Response, xtreamContext.Request.Method, cancellationToken);
                return Results.Empty;
            }

            await using var writer = new Utf8JsonWriter(xtreamContext.Response.BodyWriter);
            writer.WriteStartArray();

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var batch = new List<JsonObject>(TmdbInfoBatchSize);
            await using var payloadStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(payloadStream, topLevelValues: false, cancellationToken: cancellationToken))
            {
                if (itemService.TransformStreamItem(item, xtreamContext, source, xtreamCategoryIdMapping, seenIds) is { } transformedItem)
                    batch.Add(transformedItem);

                if (batch.Count >= TmdbInfoBatchSize)
                {
                    await WriteBatchAsync(batch, writer, xtreamContext, source, cancellationToken);

                    if (writer.BytesPending >= FlushThresholdBytes)
                        await FlushAsync(writer, xtreamContext.Response, cancellationToken);
                }
            }

            await WriteBatchAsync(batch, writer, xtreamContext, source, cancellationToken);
            writer.WriteEndArray();
            await FlushAsync(writer, xtreamContext.Response, cancellationToken);

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

    // the JSON writer only commits its buffer to the pipe; the pipe must be flushed to actually stream the response
    private static async Task FlushAsync(Utf8JsonWriter writer, HttpResponse response, CancellationToken cancellationToken)
    {
        await writer.FlushAsync(cancellationToken);
        await response.BodyWriter.FlushAsync(cancellationToken);
    }

    // enriches the batched items with their TMDB metadata, writes the ones still included, and empties the batch
    private async Task WriteBatchAsync(List<JsonObject> batch, Utf8JsonWriter writer, XtreamContext xtreamContext, XtreamSourceSnapshot source, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
            return;

        var tmdbIds = batch.Select(ItemService.ReadTmdbId).OfType<long>().Distinct().ToList();
        var tmdbInfos = await tmdbInfoService.GetAsync(xtreamContext.ContentType, tmdbIds, cancellationToken);

        foreach (var item in batch.Where(item => itemService.EnrichStreamItem(item, xtreamContext.ContentType, tmdbInfos, source)))
        {
            item.WriteTo(writer);
        }

        batch.Clear();
    }
}
