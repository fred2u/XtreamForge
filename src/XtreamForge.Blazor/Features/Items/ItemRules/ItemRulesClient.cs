using System.Net.Http.Json;
using XtreamForge.Blazor.Features.Categories;

namespace XtreamForge.Blazor.Features.Items.ItemRules;

public sealed class ItemRulesClient(HttpClient httpClient)
{
    public async Task<AdminItemRulesPayload> GetItemRulesAsync(int? sourceId, AdminContentType contentType, CancellationToken cancellationToken = default)
    {
        var query = $"/api/admin/item-rules?contentType={contentType}";
        if (sourceId is int selectedSourceId)
        {
            query += $"&sourceId={selectedSourceId}";
        }

        return await ReadAsync<AdminItemRulesPayload>(query, cancellationToken);
    }

    public async Task CreateRuleAsync(AdminItemRuleUpdate request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/admin/item-rules", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task UpdateRuleAsync(int ruleId, AdminItemRuleUpdate request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/admin/item-rules/{ruleId}", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ReorderRulesAsync(AdminItemRuleOrderUpdate request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync("/api/admin/item-rules/order", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteRuleAsync(int ruleId, int sourceId, AdminContentType contentType, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"/api/admin/item-rules/{ruleId}?sourceId={sourceId}&contentType={contentType}&confirmDelete=true", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AdminItemRulePreview> PreviewRuleAsync(int sourceId, AdminContentType contentType, string itemName, CancellationToken cancellationToken = default)
    {
        var requestUri = $"/api/admin/item-rules/preview?sourceId={sourceId}&contentType={contentType}&itemName={Uri.EscapeDataString(itemName)}";
        return ReadAsync<AdminItemRulePreview>(requestUri, cancellationToken);
    }

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
