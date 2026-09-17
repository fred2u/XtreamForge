using System.Text.Json;
using XtreamForge.Categories;
using XtreamForge.ServiceDefaults;
using XtreamForge.Source;

namespace XtreamForge.Xtream;

public sealed class XtreamCategoryProxyService(
    XtreamUpstreamClient upstreamClient,
    XtreamCategoryMappingService categoryMappingService,
    ILogger<XtreamCategoryProxyService> logger)
{
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

            using var responseMessage = await upstreamClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            if (!responseMessage.IsSuccessStatusCode)
            {
                await XtreamProxyResponseWriter.WriteAsync(responseMessage, context.Response, context.Request.Method, context.RequestAborted);
                return Results.Empty;
            }

            var upstreamCategories = await upstreamClient.ReadFromJsonAsync<List<XtreamUpstreamCategoryDto>>(responseMessage.Content, context.RequestAborted) ?? [];

            var rewrittenCategories = await categoryMappingService.SyncCategoriesAsync(
                new XtreamSourceDescriptor(destination.Protocol, destination.Host, destination.Port),
                contentType,
                [.. upstreamCategories
                    .Where(category => !string.IsNullOrWhiteSpace(category.CategoryId) && !string.IsNullOrWhiteSpace(category.CategoryName))
                    .Select(category => new DiscoveredCategory(category.CategoryId!, category.CategoryName!))],
                context.RequestAborted);

            List<XtreamCategoryResponseDto> payload = [.. rewrittenCategories.Select(category => new XtreamCategoryResponseDto(category.CategoryId, category.CategoryName))];

            await context.Response.WriteAsJsonAsync(payload, context.RequestAborted);

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
}
