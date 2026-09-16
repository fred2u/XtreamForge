using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using XtreamForge.Categories;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.Xtream;

public sealed class TmdbResolutionBackgroundService(
    TmdbResolutionQueue queue,
    XtreamUpstreamClient upstreamClient,
    StreamTmdbMappingService mappingService,
    ILogger<TmdbResolutionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ResolveAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "TMDB resolution failed for {Host}:{Port}, {ContentType}, stream {StreamId}. ErrorMessage {ErrorMessage}.",
                    ForwarderService.SanitizeForLog(request.Host),
                    request.Port,
                    request.ContentType,
                    ForwarderService.SanitizeForLog(request.StreamId),
                    XtreamCredentialRedaction.SanitizeText(exception.Message));
            }
            finally
            {
                queue.MarkCompleted(request);
            }
        }
    }

    private async Task ResolveAsync(TmdbResolutionRequest request, CancellationToken cancellationToken)
    {
        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            BuildDetailRequestUri(request));

        using var responseMessage = await upstreamClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!responseMessage.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await upstreamClient.ReadFromJsonAsync<JsonNode>(responseMessage.Content, cancellationToken);
        var tmdbId = XtreamTmdbMetadata.TryExtractTmdbId(payload);
        if (tmdbId is null)
        {
            return;
        }

        await mappingService.UpsertMappingAsync(
            request.SourceId,
            request.ContentType,
            request.StreamId,
            tmdbId.Value,
            cancellationToken);
    }

    private static Uri BuildDetailRequestUri(TmdbResolutionRequest request)
    {
        var action = request.ContentType == ContentType.Vod ? "get_vod_info" : "get_series_info";
        var idKey = request.ContentType == ContentType.Vod ? "vod_id" : "series_id";
        var path = string.IsNullOrWhiteSpace(request.Rest) ? "player_api.php" : request.Rest.TrimStart('/');

        return new Uri(
            QueryHelpers.AddQueryString(
                $"{request.Protocol}://{request.Host}:{request.Port}/{path}",
                new Dictionary<string, string?>
                {
                    ["username"] = request.Username,
                    ["password"] = request.Password,
                    ["action"] = action,
                    [idKey] = request.StreamId
                }),
            UriKind.Absolute);
    }
}
