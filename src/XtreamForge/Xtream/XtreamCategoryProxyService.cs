using System.Text.Json;
using System.Diagnostics;
using XtreamForge.Xtream;
using XtreamForge.Categories;
using XtreamForge.Source;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.Xtream;

public sealed class XtreamCategoryProxyService(
    XtreamUpstreamClient upstreamClient,
    XtreamCategoryMappingService categoryMappingService,
    ILogger<XtreamCategoryProxyService> logger)
{
    private static readonly ActivitySource ActivitySource = new("XtreamForge");

    public async Task<IResult?> TryHandleAsync(
        XtreamUpstreamDestination destination,
        XtreamRequestClassification classification,
        HttpContext context)
    {
        if (!classification.IsCategoryRewriteAction || classification.ContentType is null)
        {
            return null;
        }

        try
        {
            var contentType = classification.ContentType.Value;
            using var requestMessage = XtreamProxyHttpRequestFactory.Create(destination.TargetUri, context.Request);
            using var requestActivity = ActivitySource.StartActivity("Xtream.Categories.Request", ActivityKind.Internal);
            requestActivity?.SetTag("xtream.action", classification.Action);
            requestActivity?.SetTag("xtream.content_type", contentType.ToString());

            using var responseMessage = await SendUpstreamRequestAsync(upstreamClient, requestMessage, context.RequestAborted);
            if (!responseMessage.IsSuccessStatusCode)
            {
                await XtreamProxyResponseWriter.WriteAsync(responseMessage, context.Response, context.Request.Method, context.RequestAborted);
                return Results.Empty;
            }

            var upstreamCategories = await ReadUpstreamCategoriesAsync(upstreamClient, responseMessage.Content, context.RequestAborted);
            requestActivity?.SetTag("xtream.categories.upstream_count", upstreamCategories.Count);

            var rewrittenCategories = await categoryMappingService.SyncCategoriesAsync(
                new XtreamSourceDescriptor(destination.Protocol, destination.Host, destination.Port),
                contentType,
                upstreamCategories
                    .Select(category => new DiscoveredCategory(category.CategoryId ?? string.Empty, category.CategoryName ?? string.Empty))
                    .ToList(),
                context.RequestAborted);
            requestActivity?.SetTag("xtream.categories.output_count", rewrittenCategories.Count);

            List<XtreamCategoryResponseDto> payload;
            using (var activity = ActivitySource.StartActivity("Xtream.Categories.Output", ActivityKind.Internal))
            {
                payload = rewrittenCategories
                    .Select(category => new XtreamCategoryResponseDto(category.CategoryId, category.CategoryName))
                    .ToList();
            }

            using (var activity = ActivitySource.StartActivity("Xtream.Categories.Serialize", ActivityKind.Internal))
            {
                await context.Response.WriteAsJsonAsync(payload, context.RequestAborted);
            }

            return Results.Empty;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                "Timed out retrieving upstream categories for {Host}:{Port} and action {Action}. ErrorType {ErrorType}. ErrorMessage {ErrorMessage}.",
                ForwarderService.SanitizeForLog(destination.Host),
                destination.Port,
                ForwarderService.SanitizeForLog(classification.Action ?? "none"),
                exception.GetType().Name,
                XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                "Failed retrieving upstream categories for {Host}:{Port} and action {Action}. ErrorType {ErrorType}. ErrorMessage {ErrorMessage}.",
                ForwarderService.SanitizeForLog(destination.Host),
                destination.Port,
                ForwarderService.SanitizeForLog(classification.Action ?? "none"),
                exception.GetType().Name,
                XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                "Received an invalid category payload from {Host}:{Port} for action {Action}. ErrorType {ErrorType}. ErrorMessage {ErrorMessage}.",
                ForwarderService.SanitizeForLog(destination.Host),
                destination.Port,
                ForwarderService.SanitizeForLog(classification.Action ?? "none"),
                exception.GetType().Name,
                XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<HttpResponseMessage> SendUpstreamRequestAsync(
        XtreamUpstreamClient upstreamClient,
        HttpRequestMessage requestMessage,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("Xtream.Categories.Upstream", ActivityKind.Internal);
        return await upstreamClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task<List<XtreamUpstreamCategoryDto>> ReadUpstreamCategoriesAsync(
        XtreamUpstreamClient upstreamClient,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("Xtream.Categories.Deserialize", ActivityKind.Internal);
        return await upstreamClient.ReadFromJsonAsync<List<XtreamUpstreamCategoryDto>>(content, cancellationToken) ?? [];
    }
}
