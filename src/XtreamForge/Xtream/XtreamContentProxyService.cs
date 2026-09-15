using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Extensions;
using XtreamForge.Xtream;
using XtreamForge.Categories;

namespace XtreamForge.Xtream;

public sealed class XtreamContentProxyService(
    XtreamUpstreamClient upstreamClient,
    XtreamCategoryMappingService categoryMappingService,
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

        return classification.Action switch
        {
            "get_vod_streams" or "get_series" => await HandleStreamCollectionAsync(destination, classification, context),
            "get_vod_info" or "get_series_info" => await HandleDetailPayloadAsync(destination, classification, context),
            _ => null
        };
    }

    private async Task<IResult> HandleStreamCollectionAsync(
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification,
        HttpContext context)
    {
        try
        {
            var categoryContext = await RefreshCategoryContextAsync(destination, classification.ContentType!.Value, context, context.RequestAborted);
            var categoryRequest = ClassifyCategoryRequest(context.Request.Query);
            var responsePayload = new JsonArray();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var targetUri in GetStreamTargetUris(destination.TargetUri, context.Request.Query, categoryContext, categoryRequest))
            {
                using var requestMessage = XtreamProxyHttpRequestFactory.Create(targetUri, context.Request);
                using var responseMessage = await upstreamClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    context.RequestAborted);

                if (!responseMessage.IsSuccessStatusCode)
                {
                    await XtreamProxyResponseWriter.WriteAsync(responseMessage, context.Response, context.Request.Method, context.RequestAborted);
                    return Results.Empty;
                }

                var payload = await responseMessage.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: context.RequestAborted);
                if (payload is not JsonArray items)
                {
                    throw new JsonException("Expected an array payload for Xtream stream collections.");
                }

                AppendFilteredStreamItems(items, responsePayload, categoryContext, classification.Action!, seenIds);
            }

            return Results.Json(responsePayload);
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

            var payload = await responseMessage.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: context.RequestAborted);
            if (payload is null)
            {
                throw new JsonException("Expected a JSON payload for Xtream detail responses.");
            }

            var rewriteResult = RewriteCategoryReferences(payload, categoryContext.UpstreamToOutputCategoryIds);
            if (rewriteResult.FoundCategoryReference && rewriteResult.IncludedOutputCategoryIds.Count == 0)
            {
                return Results.NotFound();
            }

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
            && !await categoryMappingService.HasDiscoveredCategoriesAsync(sourceDescriptor, contentType, cancellationToken))
        {
            var categoryAction = contentType == ContentType.Vod ? "get_vod_categories" : "get_series_categories";
            var categoryTargetUri = BuildTargetUri(destination.TargetUri, context.Request.Query, ("action", categoryAction), ["category_id", "vod_id", "series_id"]);

            using var requestMessage = XtreamProxyHttpRequestFactory.Create(categoryTargetUri, context.Request);
            using var responseMessage = await upstreamClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            responseMessage.EnsureSuccessStatusCode();

            var upstreamCategories = await responseMessage.Content.ReadFromJsonAsync<List<XtreamUpstreamCategoryDto>>(cancellationToken: cancellationToken) ?? [];
            await categoryMappingService.SyncCategoriesAsync(
                sourceDescriptor,
                contentType,
                upstreamCategories
                    .Select(category => new DiscoveredCategory(category.CategoryId ?? string.Empty, category.CategoryName ?? string.Empty))
                    .ToList(),
                cancellationToken);

            mappings = await categoryMappingService.GetEffectiveOutputCategoryMappingsAsync(
                sourceDescriptor,
                contentType,
                cancellationToken);
        }

        var outputToUpstream = mappings.ToDictionary(
            mapping => mapping.XtreamForgeCategoryId.ToString(),
            mapping => (IReadOnlyList<string>)mapping.IncludedUpstreamCategoryIds,
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

    private static void AppendFilteredStreamItems(
        JsonArray upstreamItems,
        JsonArray resultItems,
        EffectiveCategoryContext categoryContext,
        string action,
        ISet<string> seenIds)
    {
        var identifierProperty = action == "get_series" ? "series_id" : "stream_id";

        foreach (var item in upstreamItems)
        {
            if (item is not JsonObject jsonObject)
            {
                continue;
            }

            var dedupeKey = TryGetScalarString(jsonObject[identifierProperty])
                ?? jsonObject.ToJsonString();

            var clonedObject = (JsonObject)jsonObject.DeepClone();
            var rewriteResult = RewriteCategoryReferences(clonedObject, categoryContext.UpstreamToOutputCategoryIds);
            if (!rewriteResult.FoundCategoryReference || rewriteResult.IncludedOutputCategoryIds.Count == 0)
            {
                continue;
            }

            NormalizePrimaryCategoryId(clonedObject);

            if (!seenIds.Add(dedupeKey))
            {
                continue;
            }

            resultItems.Add(clonedObject);
        }
    }

    private static void NormalizePrimaryCategoryId(JsonObject jsonObject)
    {
        var categoryId = TryGetScalarString(jsonObject["category_id"]);
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            return;
        }

        if (jsonObject["category_ids"] is not JsonArray categoryIdsArray)
        {
            return;
        }

        var firstIncludedCategoryId = categoryIdsArray
            .Select(TryGetScalarString)
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
                    var upstreamCategoryId = TryGetScalarString(property.Value);
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
                    jsonObject[property.Key] = new JsonArray(rewrittenCategoryIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
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

    private static IReadOnlyList<string> GetRewrittenCategoryIds(
        JsonNode categoryIdsNode,
        IReadOnlyDictionary<string, string> upstreamToOutputCategoryIds,
        ISet<string> includedOutputCategoryIds)
    {
        IEnumerable<string> values = categoryIdsNode switch
        {
            JsonArray jsonArray => jsonArray.Select(TryGetScalarString).OfType<string>(),
            _ => ParseCategoryIdString(TryGetScalarString(categoryIdsNode))
        };

        return values
            .Where(upstreamToOutputCategoryIds.ContainsKey)
            .Select(upstreamCategoryId => upstreamToOutputCategoryIds[upstreamCategoryId])
            .Distinct(StringComparer.Ordinal)
            .TapEach(id => includedOutputCategoryIds.Add(id))
            .ToList();
    }

    private static IEnumerable<string> ParseCategoryIdString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        if (value.StartsWith("[", StringComparison.Ordinal))
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

    private static string? TryGetScalarString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString();
            }

            if (jsonValue.TryGetValue<long>(out var longValue))
            {
                return longValue.ToString();
            }
        }

        return null;
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
            exception,
            "Timed out retrieving upstream Xtream content for {Host}:{Port} and action {Action}.",
            ForwarderService.SanitizeForLog(destination.Host),
            destination.Port,
            ForwarderService.SanitizeForLog(classification.Action ?? "none"));

        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }

    private IResult LogAndReturnBadGateway(
        Exception exception,
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification,
        string message)
    {
        logger.LogWarning(
            exception,
            "{Message} for {Host}:{Port} and action {Action}.",
            message,
            ForwarderService.SanitizeForLog(destination.Host),
            destination.Port,
            ForwarderService.SanitizeForLog(classification.Action ?? "none"));

        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    private sealed record EffectiveCategoryContext(
        IReadOnlyDictionary<string, IReadOnlyList<string>> OutputToUpstreamCategoryIds,
        IReadOnlyDictionary<string, string> UpstreamToOutputCategoryIds);

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
