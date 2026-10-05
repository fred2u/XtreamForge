using System.Text.Json;
using XtreamForge.ApiService.Endpoints.Xtream.Dto;
using XtreamForge.ApiService.Services.Catalog;
using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Endpoints.Xtream;

public class CategoriesGetEndpoint(IHttpClientFactory httpClientFactory, CategoryService categoryService, VirtualCategoryService virtualCategoryService, ILogger<CategoriesGetEndpoint> logger)
{
    public async Task<IResult> GetAsync(XtreamContext xtreamContext, CancellationToken cancellationToken)
    {
        if (xtreamContext.Action != RequestAction.GetCategories)
        {
            return Results.BadRequest("Invalid request action.");
        }

        try
        {
            using var responseMessage = await XtreamHttpForwarder.SendAsync(httpClientFactory, xtreamContext, xtreamContext.BuildTargetUri(), cancellationToken);

            if (!responseMessage.IsSuccessStatusCode)
            {
                await XtreamHttpForwarder.WriteResponseAsync(responseMessage, xtreamContext.Response, cancellationToken);
                return Results.Empty;
            }

            await using var xtreamCategoriesStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
            var xtreamCategories = await JsonSerializer.DeserializeAsync<IReadOnlyCollection<XtreamCategoryDto>>(xtreamCategoriesStream, cancellationToken: cancellationToken);

            var syncedCategories = await categoryService.SyncCategoriesAsync(xtreamContext, xtreamCategories ?? [], cancellationToken);

            // the virtual categories (recommendations, popular) come first
            IReadOnlyCollection<XtreamCategoryDto> categories = [.. virtualCategoryService.GetCategories(xtreamContext.ContentType), .. syncedCategories];

            await xtreamContext.Response.WriteAsJsonAsync(categories, cancellationToken);

            return Results.Empty;
        }
        catch (Exception exception) when (XtreamUpstreamFailure.IsUpstreamFailure(exception, cancellationToken))
        {
            return XtreamUpstreamFailure.Handle(exception, xtreamContext, logger);
        }
    }
}
