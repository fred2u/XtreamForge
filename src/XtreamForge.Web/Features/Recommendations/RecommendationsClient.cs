namespace XtreamForge.Web.Features.Recommendations;

/// <summary>HTTP client for the recommendations endpoint of ApiService.</summary>
public sealed class RecommendationsClient(HttpClient httpClient)
{
    /// <summary>Returns the movies recommended from the watch history, the best score first.</summary>
    public async Task<IReadOnlyList<RecommendationDto>> GetAsync(CancellationToken cancellationToken = default)
        => await httpClient.GetFromJsonAsync<IReadOnlyList<RecommendationDto>>("/api/admin/recommendations", cancellationToken) ?? [];
}
