using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.WatchHistory;

public class WatchHistoryActivityGetEndpoint(WatchHistoryAdminService watchHistoryAdminService)
{
    /// <summary>Movie playbacks per day of <paramref name="timeZone"/> (IANA or Windows ID; UTC when empty).</summary>
    public async Task<IResult> GetAsync(string? timeZone, CancellationToken cancellationToken = default)
    {
        var zone = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(timeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out zone))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(timeZone)] = ["The time zone is not known."]
            });
        }

        return TypedResults.Ok(await watchHistoryAdminService.GetActivityAsync(zone, cancellationToken));
    }
}
