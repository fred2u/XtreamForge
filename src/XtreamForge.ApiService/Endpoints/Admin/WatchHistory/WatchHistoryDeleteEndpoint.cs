using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.WatchHistory;

public class WatchHistoryDeleteEndpoint(WatchHistoryAdminService watchHistoryAdminService)
{
    public async Task<IResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await watchHistoryAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
