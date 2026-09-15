using System.Net.Http.Json;

namespace XtreamForge.Blazor.Features.Categories;

public sealed class CategoriesClient(HttpClient httpClient)
{
    public async Task<AdminCategoriesPayload> GetCategoriesAsync(int? sourceId, AdminContentType contentType, CancellationToken cancellationToken = default)
    {
        var query = $"/api/admin/categories?contentType={contentType}";
        if (sourceId is int selectedSourceId)
        {
            query += $"&sourceId={selectedSourceId}";
        }

        return await ReadAsync<AdminCategoriesPayload>(query, cancellationToken);
    }

    public async Task<AdminCategoryRulesPayload> GetRulesAsync(int? sourceId, AdminContentType contentType, CancellationToken cancellationToken = default)
    {
        var query = $"/api/admin/category-rules?contentType={contentType}";
        if (sourceId is int selectedSourceId)
        {
            query += $"&sourceId={selectedSourceId}";
        }

        return await ReadAsync<AdminCategoryRulesPayload>(query, cancellationToken);
    }

    public async Task UpdateCategoryMappingAsync(int upstreamCategoryRecordId, AdminCategoryMappingUpdate request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/admin/categories/{upstreamCategoryRecordId}/mapping", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<AdminSourceDiscoveryResult> DiscoverSourceAsync(AdminSourceDiscoveryCreate request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/admin/sources/discover", request, cancellationToken);
        return await ReadResponseAsync<AdminSourceDiscoveryResult>(response, cancellationToken);
    }

    public async Task CreateRuleAsync(AdminCategoryRuleUpdate request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/admin/category-rules", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task UpdateRuleAsync(int ruleId, AdminCategoryRuleUpdate request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/admin/category-rules/{ruleId}", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ReorderRulesAsync(AdminCategoryRuleOrderUpdate request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync("/api/admin/category-rules/order", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteRuleAsync(int ruleId, int sourceId, AdminContentType contentType, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"/api/admin/category-rules/{ruleId}?sourceId={sourceId}&contentType={contentType}&confirmDelete=true", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AdminCategoryRulePreview> PreviewRuleAsync(int sourceId, AdminContentType contentType, string categoryName, CancellationToken cancellationToken = default)
    {
        var requestUri = $"/api/admin/category-rules/preview?sourceId={sourceId}&contentType={contentType}&categoryName={Uri.EscapeDataString(categoryName)}";
        return ReadAsync<AdminCategoryRulePreview>(requestUri, cancellationToken);
    }

    public async Task<AdminCustomCategory> CreateCustomCategoryAsync(AdminCustomCategoryCreate request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/admin/custom-categories", request, cancellationToken);
        return await ReadResponseAsync<AdminCustomCategory>(response, cancellationToken);
    }

    public async Task UpdateCustomCategoryAsync(int customCategoryId, AdminCustomCategoryUpdate request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/admin/custom-categories/{customCategoryId}", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteCustomCategoryAsync(int customCategoryId, AdminContentType contentType, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"/api/admin/custom-categories/{customCategoryId}?contentType={contentType}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<AdminCustomCategoryUsage>> GetCustomCategoryUsagesAsync(int customCategoryId, AdminContentType contentType, CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<AdminCustomCategoryUsage>>($"/api/admin/custom-categories/{customCategoryId}/usages?contentType={contentType}", cancellationToken);

    private Task<T> ReadAsync<T>(string requestUri, CancellationToken cancellationToken) =>
        ReadResponseAsync<T>(new HttpRequestMessage(HttpMethod.Get, requestUri), cancellationToken);

    private async Task<T> ReadResponseAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        return payload ?? throw new InvalidOperationException("The backend returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await response.Content.ReadFromJsonAsync<AdminErrorResponse>(cancellationToken);
        throw new InvalidOperationException(error?.Message ?? "The backend request failed.");
    }
}
