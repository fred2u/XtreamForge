using System.Text.Json;
using System.Text.Json.Nodes;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Endpoints.Xtream;

public class ItemsGetEndpoint(
    IHttpClientFactory httpClientFactory,
    SourceService sourceService,
    CategoryService categoryService,
    ItemService itemService,
    TmdbInfoService tmdbInfoService,
    VirtualCategoryService virtualCategoryService,
    ILogger<ItemsGetEndpoint> logger)
{
    // the response is flushed in chunks rather than after every item
    private const int FlushThresholdBytes = 32 * 1024;

    // the TMDB metadata is loaded with one query per batch: preloading it for a whole catalogue would hold every overview in memory
    private const int TmdbInfoBatchSize = 500;

    private const string JsonContentType = "application/json; charset=utf-8";

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

            // a virtual category (recommendations, popular) is filled from all the categories
            var isVirtualCategory = virtualCategoryService.IsVirtualCategoryRequested(xtreamContext);
            var xtreamCategoryIdMapping = isVirtualCategory
                ? await categoryService.GetAllXtreamCategoryIdMappingAsync(xtreamContext, source, cancellationToken)
                : await categoryService.GetXtreamCategoryIdMappingAsync(xtreamContext, source, cancellationToken);
            if (xtreamCategoryIdMapping.Count == 0)
            {
                return Results.BadRequest("No categories found for the requested category_id.");
            }

            // a single upstream request: the only mapped category, or ALL filtered through the category mapping
            var upstreamCategoryId = !isVirtualCategory && !CategoryService.IsGetAll(xtreamContext) && xtreamCategoryIdMapping.Count == 1
                ? xtreamCategoryIdMapping.Keys.Single()
                : "ALL";

            // with all the categories, an item is moved to its virtual category; a requested virtual category only lists its items
            var virtualCategories = await virtualCategoryService.GetListAssignmentAsync(xtreamContext, cancellationToken);

            using var requestMessage = XtreamHttpRequestMessageFactory.Create(xtreamContext.BuildTargetUri(new KeyValuePair<string, string>("category_id", upstreamCategoryId)), xtreamContext.Request);
            var httpClient = httpClientFactory.CreateClient(XtreamProxyOptions.HttpClientName);
            using var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!responseMessage.IsSuccessStatusCode)
            {
                await XtreamHttpResponseMessageWriter.WriteResponseAsync(responseMessage, xtreamContext.Response, xtreamContext.Request.Method, cancellationToken);
                return Results.Empty;
            }

            // the media type lets the response compression apply to the largest Xtream payloads; the response is started explicitly,
            // so that a failure while streaming the list aborts the connection instead of returning an error status (see XtreamUpstreamFailure)
            xtreamContext.Response.ContentType = JsonContentType;
            await xtreamContext.Response.StartAsync(cancellationToken);

            await using var writer = new Utf8JsonWriter(xtreamContext.Response.BodyWriter);
            writer.WriteStartArray();

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var batch = new List<JsonObject>(TmdbInfoBatchSize);
            await using var payloadStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(payloadStream, topLevelValues: false, cancellationToken: cancellationToken))
            {
                if (itemService.TransformStreamItem(item, xtreamContext, source, xtreamCategoryIdMapping, seenIds) is { } transformedItem
                    && (virtualCategories is null || virtualCategories.Includes(ItemService.ReadTmdbId(transformedItem))))
                    batch.Add(transformedItem);

                if (batch.Count >= TmdbInfoBatchSize)
                {
                    await WriteBatchAsync(batch, writer, xtreamContext, source, virtualCategories, cancellationToken);

                    if (writer.BytesPending >= FlushThresholdBytes)
                        await FlushAsync(writer, xtreamContext.Response, cancellationToken);
                }
            }

            await WriteBatchAsync(batch, writer, xtreamContext, source, virtualCategories, cancellationToken);
            writer.WriteEndArray();
            await FlushAsync(writer, xtreamContext.Response, cancellationToken);

            return Results.Empty;
        }
        catch (Exception exception) when (XtreamUpstreamFailure.IsUpstreamFailure(exception, cancellationToken))
        {
            return XtreamUpstreamFailure.Handle(exception, xtreamContext, logger);
        }
    }

    // the JSON writer only commits its buffer to the pipe; the pipe must be flushed to actually stream the response
    private static async Task FlushAsync(Utf8JsonWriter writer, HttpResponse response, CancellationToken cancellationToken)
    {
        await writer.FlushAsync(cancellationToken);
        await response.BodyWriter.FlushAsync(cancellationToken);
    }

    // enriches the batched items with their TMDB metadata, writes the ones still included, and empties the batch
    private async Task WriteBatchAsync(List<JsonObject> batch, Utf8JsonWriter writer, XtreamContext xtreamContext, XtreamSourceSnapshot source, VirtualCategoryAssignment? virtualCategories, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
            return;

        var tmdbIds = batch.Select(ItemService.ReadTmdbId).OfType<long>().Distinct().ToList();
        var tmdbInfos = await tmdbInfoService.GetAsync(xtreamContext.ContentType, tmdbIds, cancellationToken);

        foreach (var item in batch.Where(item => itemService.EnrichStreamItem(item, xtreamContext.ContentType, tmdbInfos, source)))
        {
            // a TMDB ID takes its virtual category once: the next items with it keep their provider category,
            // so they are not listed in a requested virtual category
            if (virtualCategories is not null
                && !ItemService.ApplyVirtualCategory(virtualCategories, ItemService.ReadTmdbId(item), item)
                && virtualCategories.IsCategoryRequested)
                continue;

            item.WriteTo(writer);
        }

        batch.Clear();
    }
}
