using Microsoft.AspNetCore.Http.Extensions;
using System.Text.Json;
using System.Text.Json.Nodes;
using XtreamForge.Categories;
using XtreamForge.Items;
using XtreamForge.ServiceDefaults;
using XtreamForge.Source;

namespace XtreamForge.Xtream;

public sealed class XtreamContentProxyService(
    XtreamUpstreamClient upstreamClient,
    XtreamCategoryMappingService categoryMappingService,
    ItemRuleService itemRuleService,
    ItemRuleEvaluator itemRuleEvaluator,
    StreamTmdbMappingService streamTmdbMappingService,
    TmdbResolutionQueue tmdbResolutionQueue,
    SourceService sourceService,
    ILogger<XtreamContentProxyService> logger)
{
    public async Task<IResult?> TryHandleAsync(
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification,
        HttpContext context)
    {
        if (classification.ContentType is null || classification.IsCategoryRewriteAction)
        {
            return null;
        }

        var sourceDescriptor = new XtreamSourceDescriptor(destination.Protocol, destination.Host, destination.Port);
        var sourceId = await sourceService.GetSourceIdAsync(sourceDescriptor, context.RequestAborted);
        if (sourceId is null)
            return TypedResults.BadRequest("Unknown Xtream source.");

        return classification.Action switch
        {
            "get_vod_streams" or "get_series" => await HandleStreamCollectionAsync(destination, classification, sourceId.Value, context),
            "get_vod_info" or "get_series_info" => await HandleDetailPayloadAsync(destination, classification, sourceId.Value, context),
            _ => null
        };
    }

    private async Task<IResult> HandleStreamCollectionAsync(
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification,
        int sourceId,
        HttpContext context)
    {
        try
        {
            var contentType = classification.ContentType!.Value;
            var categoryContext = await RefreshCategoryContextAsync(destination, contentType, context, context.RequestAborted);
            var itemRuleSet = await itemRuleService.GetRuleSetAsync(sourceId, contentType, context.RequestAborted);
            var tmdbMappingSet = await streamTmdbMappingService.GetMappingsAsync(sourceId, contentType, context.RequestAborted);
            var categoryRequest = ClassifyCategoryRequest(context.Request.Query);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var knownTmdbMappings = new Dictionary<string, long>(tmdbMappingSet.Mappings, StringComparer.Ordinal);
            var pendingTmdbMappings = new Dictionary<string, long>(StringComparer.Ordinal);
            var requestCredentials = ExtractCredentials(context.Request.Query);
            var requestContext = new StreamCollectionContext(
                categoryContext,
                contentType,
                itemRuleSet.Rules,
                itemRuleSet.SourceId ?? tmdbMappingSet.SourceId,
                knownTmdbMappings,
                pendingTmdbMappings,
                requestCredentials,
                destination.Protocol,
                destination.Host,
                destination.Port,
                destination.Rest);
            var targetUris = GetStreamTargetUris(destination.TargetUri, context.Request.Query, categoryContext, categoryRequest).ToList();
            var upstreamResponses = new List<HttpResponseMessage>(targetUris.Count);

            try
            {
                foreach (var targetUri in targetUris)
                {
                    using var requestMessage = XtreamProxyHttpRequestFactory.Create(targetUri, context.Request);
                    var responseMessage = await upstreamClient.SendAsync(
                        requestMessage,
                        HttpCompletionOption.ResponseHeadersRead,
                        context.RequestAborted);

                    if (!responseMessage.IsSuccessStatusCode)
                    {
                        await XtreamProxyResponseWriter.WriteAsync(responseMessage, context.Response, context.Request.Method, context.RequestAborted);
                        responseMessage.Dispose();
                        return Results.Empty;
                    }

                    upstreamResponses.Add(responseMessage);
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/json; charset=utf-8";

                await using var writer = new Utf8JsonWriter(context.Response.BodyWriter);
                writer.WriteStartArray();

                foreach (var responseMessage in upstreamResponses)
                {
                    await foreach (var item in upstreamClient.ReadJsonArrayAsync(responseMessage.Content, context.RequestAborted))
                    {
                        if (!TryTransformStreamItem(item, requestContext, classification.Action!, seenIds, out var transformedItem))
                        {
                            continue;
                        }

                        transformedItem.WriteTo(writer);
                        await writer.FlushAsync(context.RequestAborted);
                    }
                }

                await PersistKnownTmdbMappingsAsync(
                    requestContext.SourceId,
                    contentType,
                    requestContext.PendingTmdbMappings,
                    destination,
                    classification,
                    context.RequestAborted);

                writer.WriteEndArray();
                await writer.FlushAsync(context.RequestAborted);
                return Results.Empty;
            }
            finally
            {
                foreach (var responseMessage in upstreamResponses)
                {
                    responseMessage.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            return LogAndReturnGatewayTimeout(exception, destination, classification);
        }
        catch (HttpRequestException exception)
        {
            return LogAndReturnBadGateway(exception, destination, classification, "Failed retrieving upstream Xtream content");
        }
        catch (JsonException exception)
        {
            return LogAndReturnBadGateway(exception, destination, classification, "Received an invalid Xtream content payload");
        }
    }

    private async Task<IResult> HandleDetailPayloadAsync(
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification,
        int sourceId,
        HttpContext context)
    {
        try
        {
            var categoryContext = await RefreshCategoryContextAsync(destination, classification.ContentType!.Value, context, context.RequestAborted);

            using var requestMessage = XtreamProxyHttpRequestFactory.Create(destination.TargetUri, context.Request);
            using var responseMessage = await upstreamClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            if (!responseMessage.IsSuccessStatusCode)
            {
                await XtreamProxyResponseWriter.WriteAsync(responseMessage, context.Response, context.Request.Method, context.RequestAborted);
                return Results.Empty;
            }

            var payload = await upstreamClient.ReadFromJsonAsync<JsonNode>(responseMessage.Content, context.RequestAborted)
                ?? throw new JsonException("Expected a JSON payload for Xtream detail responses.");

            var rewriteResult = RewriteCategoryReferences(payload, categoryContext.UpstreamToOutputCategoryIds);
            if (rewriteResult.FoundCategoryReference && rewriteResult.IncludedOutputCategoryIds.Count == 0)
            {
                return Results.NotFound();
            }

            await PersistDetailTmdbMappingAsync(payload, sourceId, classification, context.Request.Query, context.RequestAborted);
            return Results.Json(payload);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            return LogAndReturnGatewayTimeout(exception, destination, classification);
        }
        catch (HttpRequestException exception)
        {
            return LogAndReturnBadGateway(exception, destination, classification, "Failed retrieving upstream Xtream content");
        }
        catch (JsonException exception)
        {
            return LogAndReturnBadGateway(exception, destination, classification, "Received an invalid Xtream content payload");
        }
    }

    private async Task<EffectiveCategoryContext> RefreshCategoryContextAsync(
        XtreamUpstreamDestination destination,
        ContentType contentType,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var sourceDescriptor = new XtreamSourceDescriptor(destination.Protocol, destination.Host, destination.Port);
        var mappings = await categoryMappingService.GetEffectiveOutputCategoryMappingsAsync(sourceDescriptor, contentType, cancellationToken);
        if (mappings.Count == 0
            && !await sourceService.HasDiscoveredCategoriesAsync(sourceDescriptor, contentType, cancellationToken))
        {
            var categoryAction = contentType == ContentType.Vod ? "get_vod_categories" : "get_series_categories";
            var categoryTargetUri = BuildTargetUri(destination.TargetUri, context.Request.Query, ("action", categoryAction), ["category_id", "vod_id", "series_id"]);

            using var requestMessage = XtreamProxyHttpRequestFactory.Create(categoryTargetUri, context.Request);
            using var responseMessage = await upstreamClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            responseMessage.EnsureSuccessStatusCode();

            var upstreamCategories = await upstreamClient.ReadFromJsonAsync<List<XtreamUpstreamCategoryDto>>(responseMessage.Content, cancellationToken) ?? [];
            await categoryMappingService.SyncCategoriesAsync(
                sourceDescriptor,
                contentType,
                [.. upstreamCategories.Select(category => new DiscoveredCategory(category.CategoryId ?? string.Empty, category.CategoryName ?? string.Empty))],
                cancellationToken);

            mappings = await categoryMappingService.GetEffectiveOutputCategoryMappingsAsync(
                sourceDescriptor,
                contentType,
                cancellationToken);
        }

        var outputToUpstream = mappings.ToDictionary(
            mapping => mapping.XtreamForgeCategoryId.ToString(),
            mapping => mapping.IncludedUpstreamCategoryIds,
            StringComparer.Ordinal);

        var upstreamToOutput = mappings
            .SelectMany(mapping => mapping.IncludedUpstreamCategoryIds.Select(upstreamCategoryId => new KeyValuePair<string, string>(upstreamCategoryId, mapping.XtreamForgeCategoryId.ToString())))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return new EffectiveCategoryContext(outputToUpstream, upstreamToOutput);
    }

    private static IEnumerable<Uri> GetStreamTargetUris(
        Uri originalTargetUri,
        IQueryCollection originalQuery,
        EffectiveCategoryContext categoryContext,
        CategoryRequest categoryRequest)
    {
        if (categoryRequest.Mode == CategoryRequestMode.All)
        {
            yield return originalTargetUri;
            yield break;
        }

        if (!categoryContext.OutputToUpstreamCategoryIds.TryGetValue(categoryRequest.CategoryId!, out var upstreamCategoryIds)
            || upstreamCategoryIds.Count == 0)
        {
            yield break;
        }

        foreach (var upstreamCategoryId in upstreamCategoryIds)
        {
            yield return BuildTargetUri(originalTargetUri, originalQuery, ("category_id", upstreamCategoryId));
        }
    }

    private bool TryTransformStreamItem(
        JsonElement upstreamItem,
        StreamCollectionContext requestContext,
        string action,
        HashSet<string> seenIds,
        out JsonObject transformedItem)
    {
        var identifierProperty = action == "get_series" ? "series_id" : "stream_id";
        transformedItem = null!;

        if (upstreamItem.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (JsonNode.Parse(upstreamItem.GetRawText()) is not JsonObject clonedObject)
        {
            return false;
        }

        var rewriteResult = RewriteCategoryReferences(clonedObject, requestContext.CategoryContext.UpstreamToOutputCategoryIds);
        if (!rewriteResult.FoundCategoryReference || rewriteResult.IncludedOutputCategoryIds.Count == 0)
        {
            return false;
        }

        var itemRuleEvaluation = itemRuleEvaluator.Evaluate(
            new ItemRuleInput(XtreamTmdbMetadata.TryGetScalarString(clonedObject["name"])),
            requestContext.ItemRules);
        if (itemRuleEvaluation.Decision == ItemInclusionDecision.Exclude)
        {
            return false;
        }

        NormalizePrimaryCategoryId(clonedObject);

        var streamId = XtreamTmdbMetadata.TryGetScalarString(clonedObject[identifierProperty])?.Trim();
        var tmdbId = XtreamTmdbMetadata.TryParseTmdbId(clonedObject["tmdb_id"]);
        if (tmdbId is long knownTmdbId)
        {
            SetTmdbId(clonedObject, knownTmdbId);
            TrackKnownTmdbMapping(requestContext, streamId, knownTmdbId);
        }
        else if (!string.IsNullOrWhiteSpace(streamId)
            && requestContext.KnownTmdbMappings.TryGetValue(streamId, out var persistedTmdbId))
        {
            SetTmdbId(clonedObject, persistedTmdbId);
        }
        else
        {
            EnqueueTmdbResolution(requestContext, streamId);
            return false;
        }

        var dedupeKey = streamId ?? clonedObject.ToJsonString();
        if (!seenIds.Add(dedupeKey))
        {
            return false;
        }

        transformedItem = clonedObject;
        return true;
    }

    private static void NormalizePrimaryCategoryId(JsonObject jsonObject)
    {
        var categoryId = XtreamTmdbMetadata.TryGetScalarString(jsonObject["category_id"]);
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            return;
        }

        if (jsonObject["category_ids"] is not JsonArray categoryIdsArray)
        {
            return;
        }

        var firstIncludedCategoryId = categoryIdsArray
            .Select(XtreamTmdbMetadata.TryGetScalarString)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        if (firstIncludedCategoryId is not null)
        {
            jsonObject["category_id"] = firstIncludedCategoryId;
        }
    }

    private static CategoryRequest ClassifyCategoryRequest(IQueryCollection query)
    {
        var requestedCategoryId = query["category_id"].ToString();
        var normalizedCategoryId = requestedCategoryId.Trim();

        return string.IsNullOrWhiteSpace(normalizedCategoryId)
            || normalizedCategoryId.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? new CategoryRequest(CategoryRequestMode.All, null)
            : new CategoryRequest(CategoryRequestMode.Specific, normalizedCategoryId);
    }

    private static CategoryRewriteResult RewriteCategoryReferences(JsonNode node, IReadOnlyDictionary<string, string> upstreamToOutputCategoryIds)
    {
        var includedOutputCategoryIds = new HashSet<string>(StringComparer.Ordinal);
        var foundCategoryReference = RewriteCategoryReferencesCore(node, upstreamToOutputCategoryIds, includedOutputCategoryIds);
        return new CategoryRewriteResult(foundCategoryReference, includedOutputCategoryIds);
    }

    private static bool RewriteCategoryReferencesCore(
        JsonNode node,
        IReadOnlyDictionary<string, string> upstreamToOutputCategoryIds,
        ISet<string> includedOutputCategoryIds)
    {
        var foundCategoryReference = false;

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (property.Value is null)
                {
                    continue;
                }

                if (property.Key.Equals("category_id", StringComparison.OrdinalIgnoreCase))
                {
                    foundCategoryReference = true;
                    var upstreamCategoryId = XtreamTmdbMetadata.TryGetScalarString(property.Value);
                    if (upstreamCategoryId is not null && upstreamToOutputCategoryIds.TryGetValue(upstreamCategoryId, out var outputCategoryId))
                    {
                        jsonObject[property.Key] = outputCategoryId;
                        includedOutputCategoryIds.Add(outputCategoryId);
                    }
                    else
                    {
                        jsonObject[property.Key] = null;
                    }

                    continue;
                }

                if (property.Key.Equals("category_ids", StringComparison.OrdinalIgnoreCase))
                {
                    foundCategoryReference = true;
                    var rewrittenCategoryIds = GetRewrittenCategoryIds(property.Value, upstreamToOutputCategoryIds, includedOutputCategoryIds);
                    jsonObject[property.Key] = new JsonArray([.. rewrittenCategoryIds.Select(id => (JsonNode?)JsonValue.Create(id))]);
                    continue;
                }

                foundCategoryReference |= RewriteCategoryReferencesCore(property.Value, upstreamToOutputCategoryIds, includedOutputCategoryIds);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var child in jsonArray)
            {
                if (child is not null)
                {
                    foundCategoryReference |= RewriteCategoryReferencesCore(child, upstreamToOutputCategoryIds, includedOutputCategoryIds);
                }
            }
        }

        return foundCategoryReference;
    }

    private static List<string> GetRewrittenCategoryIds(
        JsonNode categoryIdsNode,
        IReadOnlyDictionary<string, string> upstreamToOutputCategoryIds,
        ISet<string> includedOutputCategoryIds)
    {
        IEnumerable<string> values = categoryIdsNode switch
        {
            JsonArray jsonArray => jsonArray.Select(XtreamTmdbMetadata.TryGetScalarString).OfType<string>(),
            _ => ParseCategoryIdString(XtreamTmdbMetadata.TryGetScalarString(categoryIdsNode))
        };

        return [.. values
            .Where(upstreamToOutputCategoryIds.ContainsKey)
            .Select(upstreamCategoryId => upstreamToOutputCategoryIds[upstreamCategoryId])
            .Distinct(StringComparer.Ordinal)
            .TapEach(id => includedOutputCategoryIds.Add(id))];
    }

    private static string[] ParseCategoryIdString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        if (value.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<string[]>(value) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static Uri BuildTargetUri(
        Uri originalTargetUri,
        IQueryCollection query,
        (string Key, string Value) replacement,
        IEnumerable<string>? removeKeys = null)
    {
        var removedKeys = new HashSet<string>(removeKeys ?? [], StringComparer.OrdinalIgnoreCase)
        {
            replacement.Key
        };

        var queryBuilder = new QueryBuilder();
        foreach (var entry in query)
        {
            if (removedKeys.Contains(entry.Key))
            {
                continue;
            }

            foreach (var value in entry.Value)
            {
                queryBuilder.Add(entry.Key, value ?? string.Empty);
            }
        }

        queryBuilder.Add(replacement.Key, replacement.Value);

        var uriBuilder = new UriBuilder(originalTargetUri)
        {
            Query = queryBuilder.ToQueryString().Value is ['?', .. var queryValue] ? queryValue : string.Empty
        };

        return uriBuilder.Uri;
    }

    private IResult LogAndReturnGatewayTimeout(
        OperationCanceledException exception,
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification)
    {
        logger.LogWarning(
            "Timed out retrieving upstream Xtream content for {Host}:{Port} and action {Action}. ErrorType {ErrorType}. ErrorMessage {ErrorMessage}.",
            ForwarderService.SanitizeForLog(destination.Host),
            destination.Port,
            ForwarderService.SanitizeForLog(classification.Action ?? "none"),
            exception.GetType().Name,
            XtreamCredentialRedaction.SanitizeText(exception.Message));

        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }

    private IResult LogAndReturnBadGateway(
        Exception exception,
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification,
        string message)
    {
        logger.LogWarning(
            "{Message} for {Host}:{Port} and action {Action}. ErrorType {ErrorType}. ErrorMessage {ErrorMessage}.",
            message,
            ForwarderService.SanitizeForLog(destination.Host),
            destination.Port,
            ForwarderService.SanitizeForLog(classification.Action ?? "none"),
            exception.GetType().Name,
            XtreamCredentialRedaction.SanitizeText(exception.Message));

        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    private sealed record EffectiveCategoryContext(
        IReadOnlyDictionary<string, IReadOnlyList<string>> OutputToUpstreamCategoryIds,
        IReadOnlyDictionary<string, string> UpstreamToOutputCategoryIds);

    private sealed record StreamCollectionContext(
        EffectiveCategoryContext CategoryContext,
        ContentType ContentType,
        IReadOnlyList<ItemRuleDefinition> ItemRules,
        int? SourceId,
        IDictionary<string, long> KnownTmdbMappings,
        IDictionary<string, long> PendingTmdbMappings,
        RequestCredentials? Credentials,
        string Protocol,
        string Host,
        int Port,
        string Rest);

    private sealed record RequestCredentials(string Username, string Password);

    private sealed record CategoryRequest(
        CategoryRequestMode Mode,
        string? CategoryId);

    private enum CategoryRequestMode
    {
        All = 1,
        Specific = 2
    }

    private sealed record CategoryRewriteResult(
        bool FoundCategoryReference,
        IReadOnlyCollection<string> IncludedOutputCategoryIds);

    private static void TrackKnownTmdbMapping(StreamCollectionContext requestContext, string? streamId, long tmdbId)
    {
        if (requestContext.SourceId is null || string.IsNullOrWhiteSpace(streamId))
        {
            return;
        }

        requestContext.KnownTmdbMappings[streamId] = tmdbId;
        requestContext.PendingTmdbMappings[streamId] = tmdbId;
    }

    private void EnqueueTmdbResolution(StreamCollectionContext requestContext, string? streamId)
    {
        if (requestContext.SourceId is null
            || string.IsNullOrWhiteSpace(streamId)
            || requestContext.Credentials is null)
        {
            return;
        }

        tmdbResolutionQueue.TryEnqueue(new TmdbResolutionRequest(
            requestContext.SourceId.Value,
            requestContext.ContentType,
            streamId,
            requestContext.Protocol,
            requestContext.Host,
            requestContext.Port,
            requestContext.Rest,
            requestContext.Credentials.Username,
            requestContext.Credentials.Password));
    }

    private async Task PersistKnownTmdbMappingsAsync(
        int? sourceId,
        ContentType contentType,
        IDictionary<string, long> pendingTmdbMappings,
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification,
        CancellationToken cancellationToken)
    {
        if (sourceId is null || pendingTmdbMappings.Count == 0)
        {
            return;
        }

        try
        {
            await streamTmdbMappingService.UpsertMappingsAsync(
                sourceId.Value,
                contentType,
                new Dictionary<string, long>(pendingTmdbMappings, StringComparer.Ordinal),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed persisting TMDB mappings for {Host}:{Port} and action {Action}. ErrorType {ErrorType}. ErrorMessage {ErrorMessage}.",
                ForwarderService.SanitizeForLog(destination.Host),
                destination.Port,
                ForwarderService.SanitizeForLog(classification.Action ?? "none"),
                exception.GetType().Name,
                XtreamCredentialRedaction.SanitizeText(exception.Message));
        }
    }

    private async Task PersistDetailTmdbMappingAsync(
        JsonNode payload,
        int sourceId,
        XtreamRequestClassification classification,
        IQueryCollection query,
        CancellationToken cancellationToken)
    {
        var tmdbId = XtreamTmdbMetadata.TryExtractTmdbId(payload);
        if (tmdbId is null)
        {
            return;
        }

        var streamId = GetDetailStreamId(classification.Action, query);
        if (string.IsNullOrWhiteSpace(streamId))
        {
            return;
        }

        await streamTmdbMappingService.UpsertMappingAsync(
            sourceId,
            classification.ContentType!.Value,
            streamId,
            tmdbId.Value,
            cancellationToken);
    }

    private static string? GetDetailStreamId(string? action, IQueryCollection query) =>
        action == "get_series_info"
            ? query["series_id"].ToString().Trim()
            : query["vod_id"].ToString().Trim();

    private static RequestCredentials? ExtractCredentials(IQueryCollection query)
    {
        var username = query["username"].ToString().Trim();
        var password = query["password"].ToString().Trim();

        return string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)
            ? null
            : new RequestCredentials(username, password);
    }

    private static void SetTmdbId(JsonObject jsonObject, long tmdbId) =>
        jsonObject["tmdb_id"] = JsonValue.Create(tmdbId);
}

internal static class EnumerableExtensions
{
    public static IEnumerable<T> TapEach<T>(this IEnumerable<T> source, Action<T> action)
    {
        foreach (var item in source)
        {
            action(item);
            yield return item;
        }
    }
}
