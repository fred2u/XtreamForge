using System.Net;
using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Web.Features.Sources;

/// <summary>HTTP client for the source administration endpoints of ApiService.</summary>
public sealed class SourcesClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<XtreamSourceSummaryDto>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        var payload = await httpClient.GetFromJsonAsync<List<XtreamSourceSummaryDto>>("/api/admin/sources", cancellationToken);
        return payload ?? [];
    }

    /// <summary>Creates a source; the API discovers its VOD and series categories with the credentials, which are not stored.</summary>
    public async Task<SourceCreateResult> CreateSourceAsync(SourceCreateRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/admin/sources", request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Conflict:
                return SourceCreateResult.Conflict;
            case HttpStatusCode.BadRequest:
                return SourceCreateResult.Invalid;
            case HttpStatusCode.BadGateway:
                return SourceCreateResult.ProviderUnavailable;
            default:
                response.EnsureSuccessStatusCode();
                return SourceCreateResult.Created;
        }
    }

    public async Task<AdminOperationResult> DeleteSourceAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/api/admin/sources/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return AdminOperationResult.NotFound;
        }

        response.EnsureSuccessStatusCode();
        return AdminOperationResult.Success;
    }
}

/// <summary>A source with the counters computed by the API, per content type.</summary>
public sealed record XtreamSourceSummaryDto(
    int Id,
    string Protocol,
    string Host,
    int Port,
    SourceContentSummaryDto Vod,
    SourceContentSummaryDto Series)
{
    public string DisplayName => $"{Protocol}://{Host}:{Port}";

    public int TotalCategories => Vod.Categories + Series.Categories;

    public int TotalEffectiveCategories => Vod.EffectiveCategories + Series.EffectiveCategories;

    public int TotalRules => Vod.CategoryRules + Vod.ItemRules + Series.CategoryRules + Series.ItemRules;

    public int TotalTmdbMappings => Vod.TmdbMappings + Series.TmdbMappings;
}

/// <summary>
/// Counters of one source and content type. Every Xtream category is either effective (sent to clients) or excluded
/// for exactly one reason: manual exclusion, disabled by the provider, or a category rule.
/// </summary>
public sealed record SourceContentSummaryDto(
    int Categories,
    int EffectiveCategories,
    int ManuallyExcludedCategories,
    int ProviderDisabledCategories,
    int RuleExcludedCategories,
    int MappedCategories,
    int CategoryRules,
    int ItemRules,
    int TmdbMappings);
public sealed record SourceCreateRequest(
    string Url,
    string Username,
    string Password);

public enum SourceCreateResult
{
    Created,
    Conflict,

    /// <summary>The API rejected the request (400): invalid or not allowed URL, missing credentials.</summary>
    Invalid,

    /// <summary>The provider could not be reached or rejected the credentials (502); nothing was created.</summary>
    ProviderUnavailable
}
