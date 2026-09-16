using System.Net.Http.Json;
using System.Text.Json;
using XtreamForge.Xtream;
using XtreamForge.Categories;
using XtreamForge.ServiceDefaults;

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

            var upstreamCategories = await responseMessage.Content.ReadFromJsonAsync<List<XtreamUpstreamCategoryDto>>(cancellationToken: context.RequestAborted) ?? [];
            var rewrittenCategories = await categoryMappingService.SyncCategoriesAsync(
                new XtreamSourceDescriptor(destination.Protocol, destination.Host, destination.Port),
                classification.ContentType.Value,
                upstreamCategories
                    .Select(category => new DiscoveredCategory(category.CategoryId ?? string.Empty, category.CategoryName ?? string.Empty))
                    .ToList(),
                context.RequestAborted);

            var payload = rewrittenCategories
                .Select(category => new XtreamCategoryResponseDto(category.CategoryId, category.CategoryName))
                .ToList();

            return TypedResults.Ok(payload);
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
