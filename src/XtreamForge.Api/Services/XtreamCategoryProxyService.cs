using System.Net.Http.Json;
using XtreamForge.Api.Models;
using XtreamForge.Infrastructure.Models;
using XtreamForge.Infrastructure.Services;

namespace XtreamForge.Api.Services;

public sealed class XtreamCategoryProxyService(
    IHttpClientFactory httpClientFactory,
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
            var httpClient = httpClientFactory.CreateClient(ForwarderService.HttpClientName);

            using var responseMessage = await httpClient.SendAsync(
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
                exception,
                "Timed out retrieving upstream categories for {Host}:{Port} and action {Action}.",
                ForwarderService.SanitizeForLog(destination.Host),
                destination.Port,
                ForwarderService.SanitizeForLog(classification.Action ?? "none"));

            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Failed retrieving upstream categories for {Host}:{Port} and action {Action}.",
                ForwarderService.SanitizeForLog(destination.Host),
                destination.Port,
                ForwarderService.SanitizeForLog(classification.Action ?? "none"));

            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
