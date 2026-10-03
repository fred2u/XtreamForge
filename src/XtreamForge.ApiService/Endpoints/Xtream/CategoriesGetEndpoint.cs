using System.Text.Json;
using XtreamForge.ApiService.Endpoints.Xtream.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Endpoints.Xtream;

public class CategoriesGetEndpoint(IHttpClientFactory httpClientFactory, CategoryService categoryService, ILogger<CategoriesGetEndpoint> logger)
{
    public async Task<IResult> GetAsync(XtreamContext xtreamContext, CancellationToken cancellationToken)
    {
        if (xtreamContext.Action != Domain.Enums.RequestAction.GetCategories)
        {
            return Results.BadRequest("Invalid request action.");
        }

        try
        {
            using var requestMessage = XtreamHttpRequestMessageFactory.Create(xtreamContext.BuildTargetUri(), xtreamContext.Request);

            var httpClient = httpClientFactory.CreateClient(XtreamProxyOptions.HttpClientName);

            using var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!responseMessage.IsSuccessStatusCode)
            {
                await XtreamHttpResponseMessageWriter.WriteResponseAsync(responseMessage, xtreamContext.Response, xtreamContext.Request.Method, cancellationToken);
                return Results.Empty;
            }

            await using var xtreamCategoriesStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
            var xtreamCategories = await JsonSerializer.DeserializeAsync<IReadOnlyCollection<XtreamCategoryDto>>(xtreamCategoriesStream, cancellationToken: cancellationToken);

            var syncedCategories = await categoryService.SyncCategoriesAsync(xtreamContext, xtreamCategories ?? [], cancellationToken);

            await XtreamHttpResponseMessageWriter.WriteAsJsonAsync(syncedCategories, xtreamContext.Response, cancellationToken);

            return Results.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(exception, "ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
