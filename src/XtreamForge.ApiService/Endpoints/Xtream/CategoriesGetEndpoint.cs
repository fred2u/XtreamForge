using System.Text.Json;
using XtreamForge.ApiService.Endpoints.Xtream.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Endpoints.Xtream;

public class CategoriesGetEndpoint(IHttpClientFactory httpClientFactory, CategoryService categoryService, VirtualCategoryService virtualCategoryService, ILogger<CategoriesGetEndpoint> logger)
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

            // the virtual categories (recommendations, popular) come first
            IReadOnlyCollection<XtreamCategoryDto> categories = [.. virtualCategoryService.GetCategories(xtreamContext.ContentType), .. syncedCategories];

            await XtreamHttpResponseMessageWriter.WriteAsJsonAsync(categories, xtreamContext.Response, cancellationToken);

            return Results.Empty;
        }
        catch (Exception exception) when (XtreamUpstreamFailure.IsUpstreamFailure(exception, cancellationToken))
        {
            return XtreamUpstreamFailure.Handle(exception, xtreamContext, logger);
        }
    }
}
