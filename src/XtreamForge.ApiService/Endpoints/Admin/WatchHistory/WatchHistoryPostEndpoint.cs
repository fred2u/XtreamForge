using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.WatchHistory.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.WatchHistory;

public class WatchHistoryPostEndpoint(WatchHistoryAdminService watchHistoryAdminService, IOptions<TmdbOptions> tmdbOptions)
{
    /// <summary>Adds a playback, started now, of a TMDB metadata entry to the watch history.</summary>
    public async Task<IResult> PostAsync(int tmdbInfoId, CancellationToken cancellationToken = default)
    {
        var item = await watchHistoryAdminService.AddAsync(tmdbInfoId, cancellationToken);

        return item is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(AdminWatchHistoryEntryDto.From(item, tmdbOptions.Value.ImageBaseUrl));
    }
}
