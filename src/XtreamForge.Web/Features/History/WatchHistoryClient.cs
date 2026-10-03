using System.Globalization;
using System.Net;
using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Web.Features.History;

/// <summary>HTTP client for the watch history endpoints of ApiService.</summary>
public sealed class WatchHistoryClient(HttpClient httpClient)
{
    /// <summary>Returns a page of the watch history, the most recent playback first.</summary>
    public async Task<WatchHistoryPageDto> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = $"skip={skip.ToString(CultureInfo.InvariantCulture)}&take={take.ToString(CultureInfo.InvariantCulture)}";

        return await httpClient.GetFromJsonAsync<WatchHistoryPageDto>($"/api/admin/watch-history?{query}", cancellationToken)
            ?? new WatchHistoryPageDto([], 0);
    }

    /// <summary>Adds a playback, started now, of a TMDB metadata entry (TMDB infos screen) to the watch history.</summary>
    public async Task<AdminOperationResult> AddAsync(int tmdbInfoId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"/api/admin/tmdb-infos/{tmdbInfoId.ToString(CultureInfo.InvariantCulture)}/watch-history", null, cancellationToken);
        return ToResult(response);
    }

    public async Task<AdminOperationResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/api/admin/watch-history/{id.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
        return ToResult(response);
    }

    private static AdminOperationResult ToResult(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return AdminOperationResult.NotFound;
        }

        response.EnsureSuccessStatusCode();
        return AdminOperationResult.Success;
    }
}
