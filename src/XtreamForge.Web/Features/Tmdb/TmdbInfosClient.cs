using System.Net;
using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Web.Features.Tmdb;

/// <summary>HTTP client for the TMDB metadata administration endpoints of ApiService (the TMDB rules use <see cref="CategoriesClient"/>).</summary>
public sealed class TmdbInfosClient(HttpClient httpClient)
{
    public async Task<TmdbInfoPageDto> GetPageAsync(ContentType contentType, TmdbInfoFilter filter, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = await httpClient.GetFromJsonAsync<TmdbInfoPageDto>($"/api/admin/tmdb-infos?{filter.ToQueryString(contentType, skip, take)}", cancellationToken);
        return page ?? new TmdbInfoPageDto([], 0, 0, 0, 0, 0, []);
    }

    /// <summary>Returns every value of an entry, or null when it no longer exists.</summary>
    public async Task<TmdbInfoDetailsDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/api/admin/tmdb-infos/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TmdbInfoDetailsDto>(cancellationToken);
    }

    public async Task<AdminOperationResult> SetExcludedAsync(int id, bool isExcluded, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PatchAsJsonAsync($"/api/admin/tmdb-infos/{id}", new TmdbInfoPatchRequest(isExcluded), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return AdminOperationResult.NotFound;
        }

        response.EnsureSuccessStatusCode();
        return AdminOperationResult.Success;
    }
}
