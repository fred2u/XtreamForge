using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Services;

public class ItemService(TmdbIdRetrieverQueue tmdbIdRetrieverQueue, TmdbInfoQueue tmdbInfoQueue, IOptions<TmdbOptions> tmdbOptions, TimeProvider timeProvider)
{
    /// <summary>
    /// Filters and rewrites one upstream item in place; returns null when the item must not be returned.
    /// The TMDB metadata is applied afterwards by <see cref="EnrichStreamItem"/>, once loaded for a batch of items.
    /// </summary>
    public JsonObject? TransformStreamItem(JsonNode? item, XtreamContext xtreamContext, XtreamSourceSnapshot source, IReadOnlyDictionary<string, int> xtreamCategoryIdMapping, HashSet<string> seenIds)
    {
        if (item is not JsonObject transformedItem)
            return null;

        // extract the identifier based on the content type (VOD or Series)
        var identifierKey = xtreamContext.ContentType == ContentType.Vod ? "stream_id" : "series_id";
        if (!transformedItem.TryGetPropertyValue(identifierKey, out var streamIdNode) || streamIdNode is null)
            return null;
        string streamId = streamIdNode.ToString();
        if (string.IsNullOrWhiteSpace(streamId))
            return null;

        // check if already added 
        if (!seenIds.Add(streamId))
            return null;

        // rewrite category references
        if (!RewriteCategoryReferences(transformedItem, xtreamCategoryIdMapping))
            return null;

        // apply item filter rules before update tmdb info
        var inclusionDecision = ItemRuleService.ApplyRules(transformedItem, source.ItemRules);
        if (inclusionDecision == InclusionDecision.Exclude)
            return null;

        // extract the tmdb_id or retrieve it
        if (!EnsureTmdbId(streamId, transformedItem, xtreamContext, source))
            return null;

        return transformedItem;
    }

    /// <summary>
    /// Evaluates the TMDB rules on the TMDB metadata of a list item returned by <see cref="TransformStreamItem"/>, then applies the metadata;
    /// returns false when the item must not be returned. An item that cannot be enriched (invalid TMDB ID, metadata not loaded yet)
    /// or whose metadata is excluded (manually or by a TMDB rule) is not returned; a missing or outdated metadata is enqueued for loading,
    /// so the item appears on a later request.
    /// </summary>
    public bool EnrichStreamItem(JsonObject item, ContentType contentType, TmdbInfoLookup tmdbInfos, XtreamSourceSnapshot source)
    {
        if (ReadTmdbId(item) is not { } tmdbId)
            return false;

        return ApplyTmdbInfo(contentType, tmdbId, tmdbInfos, source, item);
    }

    /// <summary>
    /// Evaluates the TMDB rules and applies the TMDB metadata to a payload returned by <see cref="TransformInfo"/> (<c>info</c> and
    /// <c>movie_data</c> sections); returns false when the item must not be returned, as in <see cref="EnrichStreamItem"/>.
    /// </summary>
    public bool EnrichInfo(JsonObject payload, ContentType contentType, long? tmdbId, TmdbInfoLookup tmdbInfos, XtreamSourceSnapshot source)
    {
        var itemSectionName = contentType == ContentType.Vod ? "movie_data" : "info";
        if (tmdbId is null || payload[itemSectionName] is not JsonObject item)
            return false;

        JsonObject[] sections = contentType == ContentType.Vod && payload["info"] is JsonObject info
            ? [item, info]
            : [item];

        return ApplyTmdbInfo(contentType, tmdbId.Value, tmdbInfos, source, sections);
    }

    /// <summary>
    /// Reads the TMDB ID of a list item; null when it is missing or not a positive number.
    /// </summary>
    public static long? ReadTmdbId(JsonObject? item)
    {
        if (item is null || !item.TryGetPropertyValue("tmdb_id", out var tmdbIdNode) || tmdbIdNode is null)
            return null;

        return long.TryParse(tmdbIdNode.ToString().Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId) && tmdbId > 0
            ? tmdbId
            : null;
    }

    /// <summary>
    /// Reads the TMDB ID of a <c>get_vod_info</c> / <c>get_series_info</c> payload from <c>info</c>, or from <c>movie_data</c> for VOD.
    /// </summary>
    public static long? ReadInfoTmdbId(JsonObject payload, ContentType contentType)
    {
        var tmdbId = ReadTmdbId(payload["info"] as JsonObject);
        if (tmdbId is null && contentType == ContentType.Vod)
            tmdbId = ReadTmdbId(payload["movie_data"] as JsonObject);

        return tmdbId;
    }

    /// <summary>
    /// Filters and rewrites a <c>get_vod_info</c> / <c>get_series_info</c> payload in place; returns null when the item must not be returned.
    /// The item fields (name, categories) are read from <c>movie_data</c> for VOD and from <c>info</c> for series.
    /// A stored TMDB mapping of the stream replaces the provider TMDB ID, so that a manual correction applies as in the item lists.
    /// Unlike <see cref="TransformStreamItem"/>, no TMDB ID lookup is enqueued. The TMDB metadata is applied afterwards by <see cref="EnrichInfo"/>.
    /// </summary>
    public static JsonObject? TransformInfo(JsonNode? payload, string streamId, ContentType contentType, XtreamSourceSnapshot source, IReadOnlyDictionary<string, int> xtreamCategoryIdMapping)
    {
        if (payload is not JsonObject transformedPayload)
            return null;

        var itemSectionName = contentType == ContentType.Vod ? "movie_data" : "info";
        if (transformedPayload[itemSectionName] is not JsonObject item)
            return null;

        // rewrite category references, as for the item lists
        if (!RewriteCategoryReferences(item, xtreamCategoryIdMapping))
            return null;

        var info = transformedPayload["info"] as JsonObject;
        if (contentType == ContentType.Vod && info is not null)
            _ = RewriteCategoryReferences(info, xtreamCategoryIdMapping);

        if (ItemRuleService.ApplyRules(item, source.ItemRules) == InclusionDecision.Exclude)
            return null;

        // the mapped tmdb_id wins (it may have been corrected manually) and is exposed in the info section, where clients read it;
        // otherwise the provider tmdb_id is kept
        if (!source.StreamTmdbMappings.TryGetValue(streamId, out var tmdbId))
            return HasTmdbId(info) || (contentType == ContentType.Vod && HasTmdbId(item)) ? transformedPayload : null;

        if (info is null)
        {
            info = new JsonObject();
            transformedPayload["info"] = info;
        }
        info["tmdb_id"] = tmdbId.ToString();

        if (contentType == ContentType.Vod && item.ContainsKey("tmdb_id"))
            item["tmdb_id"] = tmdbId.ToString();

        return transformedPayload;
    }

    private static bool RewriteCategoryReferences(JsonObject item, IReadOnlyDictionary<string, int> xtreamCategoryIdMapping)
    {
        if (!item.TryGetPropertyValue("category_id", out var categoryIdNode) || categoryIdNode is null)
            return false;

        string xtreamCategoryId = categoryIdNode.ToString();

        if (!xtreamCategoryIdMapping.TryGetValue(xtreamCategoryId, out var categoryId))
            return false;

        item["category_id"] = categoryId.ToString();

        if (item.ContainsKey("category_ids"))
            item["category_ids"] = new JsonArray(categoryId.ToString());

        return true;
    }

    private bool EnsureTmdbId(string streamId, JsonObject item, XtreamContext xtreamContext, XtreamSourceSnapshot source)
    {
        if (HasTmdbId(item))
            return true;

        if (source.StreamTmdbMappings.TryGetValue(streamId, out var tmdbId))
        {
            item["tmdb_id"] = tmdbId.ToString();
            return true;
        }

        // Background service to retrieve the tmdb_id for streamId and update the mapping in the database,
        // unless a previous lookup failed or found nothing and the next one is not due yet
        if (!source.DeferredTmdbLookups.Contains(streamId))
            EnqueueTmdbIdRetrieval(streamId, item, xtreamContext, source.Id);

        return false;
    }

    private static bool HasTmdbId(JsonObject? item)
        => item is not null && item.TryGetPropertyValue("tmdb_id", out var tmdbIdNode) && tmdbIdNode is not null && !string.IsNullOrWhiteSpace(tmdbIdNode.ToString());

    // returns true when the TMDB rules include the item and its metadata was applied; a missing metadata, or one due for a (re)load, is enqueued
    private bool ApplyTmdbInfo(ContentType contentType, long tmdbId, TmdbInfoLookup tmdbInfos, XtreamSourceSnapshot source, params JsonObject[] targets)
    {
        // a manually excluded entry is excluded without being loaded again
        if (tmdbInfos.ExcludedTmdbIds.Contains(tmdbId))
            return false;

        tmdbInfos.Infos.TryGetValue(tmdbId, out var tmdbInfo);

        if ((tmdbInfo is null || tmdbInfo.NextLoadAtUtc <= timeProvider.GetUtcNow()) && !string.IsNullOrWhiteSpace(tmdbOptions.Value.ApiKey))
            tmdbInfoQueue.TryEnqueue(new TmdbInfoRequest(contentType, tmdbId));

        if (tmdbInfo?.LoadedAtUtc is null)
            return false;

        // the TMDB rules replace a second evaluation of the item rules on the enriched item
        if (TmdbRuleService.Evaluate(tmdbInfo, source.TmdbRules).Decision == InclusionDecision.Exclude)
            return false;

        foreach (var target in targets)
        {
            TmdbItemEnricher.Apply(target, tmdbInfo, tmdbOptions.Value.ImageBaseUrl);
        }

        return true;
    }

    private void EnqueueTmdbIdRetrieval(string streamId, JsonObject item, XtreamContext xtreamContext, int xtreamSourceId)
    {
        var username = xtreamContext.Request.Query["username"].ToString();
        var password = xtreamContext.Request.Query["password"].ToString();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return;

        // the stream_icon of a VOD item is often the TMDB poster, a strong matching signal
        var streamIcon = item.TryGetPropertyValue("stream_icon", out var streamIconNode) ? streamIconNode?.ToString() : null;

        tmdbIdRetrieverQueue.TryEnqueue(new TmdbIdRetrieverRequest(
            xtreamSourceId,
            xtreamContext.Protocol,
            xtreamContext.Host,
            xtreamContext.Port,
            username,
            password,
            streamId,
            xtreamContext.ContentType,
            streamIcon));
    }
}
