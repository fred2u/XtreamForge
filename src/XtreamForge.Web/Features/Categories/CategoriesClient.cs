using System.Net;

namespace XtreamForge.Web.Features.Categories;

/// <summary>HTTP client for the category administration endpoints of ApiService.</summary>
public sealed class CategoriesClient(HttpClient httpClient)
{
    // ─── Sources ────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<XtreamSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<XtreamSourceDto>("/api/admin/sources", cancellationToken);

    // ─── Custom categories ──────────────────────────────────────────────────

    public Task<IReadOnlyList<CustomCategoryDto>> GetCustomCategoriesAsync(ContentType contentType, CancellationToken cancellationToken = default) =>
        GetListAsync<CustomCategoryDto>($"/api/admin/custom-categories?contentType={contentType}", cancellationToken);

    public async Task<AdminOperationResult> CreateCustomCategoryAsync(CustomCategoryRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/admin/custom-categories", request, cancellationToken);
        return ToResult(response);
    }

    public async Task<AdminOperationResult> UpdateCustomCategoryAsync(int id, CustomCategoryRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/api/admin/custom-categories/{id}", request, cancellationToken);
        return ToResult(response);
    }

    public async Task<AdminOperationResult> DeleteCustomCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/api/admin/custom-categories/{id}", cancellationToken);
        return ToResult(response);
    }

    /// <summary>
    /// Lists the Xtream categories mapped to a custom category. The API has no dedicated endpoint, so this loads
    /// the Xtream categories of every source and content type (a few requests: one per source and content type).
    /// </summary>
    public async Task<IReadOnlyList<CustomCategoryMapping>> GetCustomCategoryMappingsAsync(int customCategoryId, CancellationToken cancellationToken = default)
    {
        var sources = await GetSourcesAsync(cancellationToken);
        var requests = sources.SelectMany(source => Enum.GetValues<ContentType>().Select(async contentType =>
        {
            var categories = await GetXtreamCategoriesAsync(source.Id, contentType, cancellationToken);
            return categories
                .Where(category => category.CustomCategoryId == customCategoryId)
                .Select(category => new CustomCategoryMapping(source, category));
        }));

        var results = await Task.WhenAll(requests);
        return [.. results.SelectMany(mappings => mappings)];
    }

    // ─── Xtream categories ──────────────────────────────────────────────────

    public Task<IReadOnlyList<XtreamCategoryDto>> GetXtreamCategoriesAsync(int sourceId, ContentType contentType, CancellationToken cancellationToken = default) =>
        GetListAsync<XtreamCategoryDto>($"/api/admin/sources/{sourceId}/xtream-categories?contentType={contentType}", cancellationToken);

    public async Task<AdminOperationResult> PatchXtreamCategoryAsync(int id, XtreamCategoryPatchRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PatchAsJsonAsync($"/api/admin/xtream-categories/{id}", request, cancellationToken);
        return ToResult(response);
    }

    // ─── Category, item, and TMDB rules ─────────────────────────────────────
    // The rules have the same shape and the same endpoints, under "category-rules" or "item-rules" of a source,
    // or under "tmdb-rules" for the TMDB rules, which have no source.

    public Task<IReadOnlyList<RuleDto>> GetRulesAsync(RuleKind kind, int? sourceId, ContentType contentType, CancellationToken cancellationToken = default) =>
        GetListAsync<RuleDto>($"{RulesPath(kind, sourceId)}?contentType={contentType}", cancellationToken);

    public async Task<AdminOperationResult> CreateRuleAsync(RuleKind kind, int? sourceId, RuleRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(RulesPath(kind, sourceId), request, cancellationToken);
        return ToResult(response);
    }

    public async Task<AdminOperationResult> UpdateRuleAsync(RuleKind kind, int id, RuleRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/api/admin/{RulesSegment(kind)}/{id}", request, cancellationToken);
        return ToResult(response);
    }

    public async Task<AdminOperationResult> DeleteRuleAsync(RuleKind kind, int id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/api/admin/{RulesSegment(kind)}/{id}", cancellationToken);
        return ToResult(response);
    }

    /// <summary>Stores a new rule order in one atomic operation; the API assigns the sequences.</summary>
    public async Task<AdminOperationResult> ReorderRulesAsync(RuleKind kind, int? sourceId, RuleOrderRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"{RulesPath(kind, sourceId)}/order", request, cancellationToken);
        return ToResult(response);
    }

    private static string RulesSegment(RuleKind kind) => kind switch
    {
        RuleKind.Item => "item-rules",
        RuleKind.Tmdb => "tmdb-rules",
        _ => "category-rules"
    };

    // the rules of a scope: global for the TMDB rules, per source otherwise
    private static string RulesPath(RuleKind kind, int? sourceId)
    {
        if (kind == RuleKind.Tmdb)
            return $"/api/admin/{RulesSegment(kind)}";

        return sourceId is { } id
            ? $"/api/admin/sources/{id}/{RulesSegment(kind)}"
            : throw new ArgumentException($"A source is required for the {kind} rules.", nameof(sourceId));
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        var payload = await httpClient.GetFromJsonAsync<List<T>>(requestUri, cancellationToken);
        return payload ?? [];
    }

    private static AdminOperationResult ToResult(HttpResponseMessage response)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return AdminOperationResult.NotFound;
            case HttpStatusCode.Conflict:
                return AdminOperationResult.Conflict;
            case HttpStatusCode.BadRequest:
                return AdminOperationResult.Invalid;
            default:
                response.EnsureSuccessStatusCode();
                return AdminOperationResult.Success;
        }
    }
}
