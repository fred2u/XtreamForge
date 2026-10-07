using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Web.Features.Recommendations;

/// <summary>HTTP client for the recommendations endpoint of ApiService.</summary>
public sealed class RecommendationsClient(HttpClient httpClient)
{
    /// <summary>Returns the movies (VOD) or TV shows (series) recommended from the watch history, the best score first.</summary>
    public async Task<IReadOnlyList<RecommendationDto>> GetAsync(ContentType contentType, CancellationToken cancellationToken = default)
        => await httpClient.GetFromJsonAsync<IReadOnlyList<RecommendationDto>>($"/api/admin/recommendations?contentType={contentType}", cancellationToken) ?? [];
}
