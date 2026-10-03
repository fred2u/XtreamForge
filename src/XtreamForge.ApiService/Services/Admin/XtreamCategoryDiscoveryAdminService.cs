using System.Text.Json;
using XtreamForge.ApiService.Endpoints.Xtream.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Services.Admin;

public class XtreamCategoryDiscoveryAdminService(IHttpClientFactory httpClientFactory, CategoryService categoryService, ILogger<XtreamCategoryDiscoveryAdminService> logger)
{
    public async Task<XtreamSource?> DiscoverAsync(string protocol, string host, int port, string username, string password, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<XtreamCategoryDto> vodCategories;
        IReadOnlyCollection<XtreamCategoryDto> seriesCategories;

        // fetch everything before persisting so that nothing is saved when the upstream source is unreachable
        try
        {
            vodCategories = await GetCategoriesAsync(protocol, host, port, username, password, "get_vod_categories", cancellationToken);
            seriesCategories = await GetCategoriesAsync(protocol, host, port, username, password, "get_series_categories", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or JsonException)
        {
            logger.LogWarning(exception, "ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));

            return null;
        }

        await categoryService.SyncCategoriesAsync(protocol, host, port, ContentType.Vod, vodCategories, cancellationToken);

        return await categoryService.SyncCategoriesAsync(protocol, host, port, ContentType.Series, seriesCategories, cancellationToken);
    }

    private async Task<IReadOnlyCollection<XtreamCategoryDto>> GetCategoriesAsync(string protocol, string host, int port, string username, string password, string action, CancellationToken cancellationToken)
    {
        var uriBuilder = new UriBuilder(protocol, host, port, "/player_api.php")
        {
            Query = $"username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&action={action}"
        };

        var httpClient = httpClientFactory.CreateClient(XtreamProxyOptions.HttpClientName);

        using var responseMessage = await httpClient.GetAsync(uriBuilder.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        responseMessage.EnsureSuccessStatusCode();

        await using var stream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
        var categories = await JsonSerializer.DeserializeAsync<IReadOnlyCollection<XtreamCategoryDto>>(stream, cancellationToken: cancellationToken);

        return categories ?? [];
    }
}
