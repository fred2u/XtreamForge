using System.Net;
using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Web.Features.Tmdb;

/// <summary>HTTP client for the stream TMDB mapping administration endpoints of ApiService.</summary>
public sealed class TmdbMappingsClient(HttpClient httpClient)
{
    /// <summary>Returns a page of the mappings of a source, or null when the source no longer exists.</summary>
    public async Task<TmdbMappingPageDto?> GetPageAsync(int sourceId, ContentType contentType, TmdbMappingFilter filter, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        using var response = await httpClient.GetAsync($"/api/admin/sources/{sourceId}/tmdb-mappings?{filter.ToQueryString(contentType, skip, take)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TmdbMappingPageDto>(cancellationToken) ?? new TmdbMappingPageDto([], 0, 0, 0);
    }

    public async Task<AdminOperationResult> SetTmdbIdAsync(int id, long tmdbId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PatchAsJsonAsync($"/api/admin/tmdb-mappings/{id}", new TmdbMappingPatchRequest(tmdbId), cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return AdminOperationResult.NotFound;
            case HttpStatusCode.BadRequest:
                return AdminOperationResult.Invalid;
            default:
                response.EnsureSuccessStatusCode();
                return AdminOperationResult.Success;
        }
    }
}
