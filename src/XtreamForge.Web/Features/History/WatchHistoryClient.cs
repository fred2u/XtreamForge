using System.Globalization;

namespace XtreamForge.Web.Features.History;

/// <summary>HTTP client for the watch history endpoint of ApiService.</summary>
public sealed class WatchHistoryClient(HttpClient httpClient)
{
    /// <summary>Returns a page of the watch history, the most recent playback first.</summary>
    public async Task<WatchHistoryPageDto> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = $"skip={skip.ToString(CultureInfo.InvariantCulture)}&take={take.ToString(CultureInfo.InvariantCulture)}";

        return await httpClient.GetFromJsonAsync<WatchHistoryPageDto>($"/api/admin/watch-history?{query}", cancellationToken)
            ?? new WatchHistoryPageDto([], 0);
    }
}
