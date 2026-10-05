using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Queues;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.ApiService.Services.TmdbInfos;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;

namespace XtreamForge.ApiService.Services.TmdbIdRetriever;

public class TmdbIdRetrieverService(IHttpClientFactory httpClientFactory, XtreamForgeDbContext dbContext, TmdbIdMatcher tmdbIdMatcher, TmdbInfoQueue tmdbInfoQueue, TimeProvider timeProvider)
    : IQueueProcessor<TmdbIdRetrieverRequest>
{
    public static readonly TimeSpan FirstLookupRetryDelay = TimeSpan.FromDays(1);
    public static readonly TimeSpan MaximumLookupRetryDelay = TimeSpan.FromDays(30);

    Task<bool> IQueueProcessor<TmdbIdRetrieverRequest>.ProcessAsync(TmdbIdRetrieverRequest request, CancellationToken cancellationToken)
        => RetrieveAsync(request, cancellationToken);

    /// <summary>
    /// Returns true when a TMDB ID was found and persisted; false when the stream was already mapped, its next lookup is not due yet, or nothing was found.
    /// A lookup without result or failing schedules the next one after a delay doubling from <see cref="FirstLookupRetryDelay"/> up to <see cref="MaximumLookupRetryDelay"/>.
    /// A found TMDB ID, from the provider or from the TMDB search, is enqueued for the loading of its TMDB metadata.
    /// </summary>
    public async Task<bool> RetrieveAsync(TmdbIdRetrieverRequest request, CancellationToken cancellationToken)
    {
        var mapping = await dbContext.StreamTmdbMappings.SingleOrDefaultAsync(
            mapping => mapping.XtreamSourceId == request.XtreamSourceId && mapping.ContentType == request.Type && mapping.StreamId == request.StreamId,
            cancellationToken);
        if (mapping is not null && (mapping.TmdbId is not null || mapping.NextLookupAtUtc > timeProvider.GetUtcNow()))
            return false;

        mapping ??= dbContext.StreamTmdbMappings.Add(new StreamTmdbMapping
        {
            XtreamSourceId = request.XtreamSourceId,
            ContentType = request.Type,
            StreamId = request.StreamId
        }).Entity;

        long? tmdbId;
        try
        {
            tmdbId = await FindTmdbIdAsync(request, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // a failing lookup is deferred like a lookup without result, so that a broken upstream is not called on every catalogue request
            await DeferNextLookupAsync(mapping, cancellationToken);
            throw;
        }

        if (tmdbId is null)
        {
            await DeferNextLookupAsync(mapping, cancellationToken);
            return false;
        }

        mapping.TmdbId = tmdbId;
        mapping.NextLookupAtUtc = null;
        mapping.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);

        // the metadata loader ignores the request when the metadata is already loaded and not due for a refresh
        tmdbInfoQueue.TryEnqueue(new TmdbInfoRequest(request.Type, tmdbId.Value));

        return true;
    }

    private async Task<long?> FindTmdbIdAsync(TmdbIdRetrieverRequest request, CancellationToken cancellationToken)
    {
        using var providerInfo = await GetProviderInfoAsync(request, cancellationToken);

        // the provider tmdb_id is trusted first, the TMDB search is only a fallback
        return ReadTmdbId(providerInfo.RootElement, request.Type)
            ?? await tmdbIdMatcher.FindTmdbIdAsync(request.Type, providerInfo.RootElement, request.StreamIcon, cancellationToken);
    }

    private async Task DeferNextLookupAsync(StreamTmdbMapping mapping, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        mapping.LookupAttemptCount++;
        mapping.NextLookupAtUtc = now + RetryDelay.Get(mapping.LookupAttemptCount, FirstLookupRetryDelay, MaximumLookupRetryDelay);
        mapping.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<JsonDocument> GetProviderInfoAsync(TmdbIdRetrieverRequest request, CancellationToken cancellationToken)
    {
        var (action, idParameter) = request.Type == ContentType.Vod
            ? ("get_vod_info", "vod_id")
            : ("get_series_info", "series_id");

        var uriBuilder = new UriBuilder(request.Protocol, request.Host, request.Port, "/player_api.php")
        {
            Query = $"username={Uri.EscapeDataString(request.Username)}&password={Uri.EscapeDataString(request.Password)}&action={action}&{idParameter}={Uri.EscapeDataString(request.StreamId)}"
        };

        var httpClient = httpClientFactory.CreateClient(XtreamProxyOptions.HttpClientName);

        using var responseMessage = await httpClient.GetAsync(uriBuilder.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        responseMessage.EnsureSuccessStatusCode();

        await using var stream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);

        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static long? ReadTmdbId(JsonElement root, ContentType type)
    {
        var tmdbId = ReadTmdbId(root, "info");
        if (tmdbId is null && type == ContentType.Vod)
            tmdbId = ReadTmdbId(root, "movie_data");

        return tmdbId;
    }

    private static long? ReadTmdbId(JsonElement root, string sectionName)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(sectionName, out var section)
            || section.ValueKind != JsonValueKind.Object
            || !section.TryGetProperty("tmdb_id", out var tmdbIdElement))
            return null;

        var rawValue = tmdbIdElement.ValueKind switch
        {
            JsonValueKind.String => tmdbIdElement.GetString(),
            JsonValueKind.Number => tmdbIdElement.GetRawText(),
            _ => null
        };

        return long.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId) && tmdbId > 0
            ? tmdbId
            : null;
    }
}
