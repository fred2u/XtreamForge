using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.WatchHistory.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.WatchHistory;

public static class WatchHistoryEndpoints
{
    public static async Task<IResult> GetListAsync(
        [AsParameters] WatchHistoryListQuery query,
        WatchHistoryAdminService watchHistoryAdminService,
        IOptions<TmdbOptions> tmdbOptions,
        CancellationToken cancellationToken = default)
    {
        if (query.ContentType is { } contentType && (contentType == ContentType.Undefined || !Enum.IsDefined(contentType)))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(WatchHistoryListQuery.ContentType)] = ["The content type is not valid."]
            });
        }

        var page = await watchHistoryAdminService.GetPageAsync(query, cancellationToken);
        var imageBaseUrl = tmdbOptions.Value.ImageBaseUrl;

        return TypedResults.Ok(new AdminWatchHistoryPageDto(
            [.. page.Items.Select(item => AdminWatchHistoryEntryDto.From(item, imageBaseUrl))],
            page.MatchingCount));
    }

    /// <summary>Movie playbacks per day of <paramref name="timeZone"/> (IANA or Windows ID; UTC when empty).</summary>
    public static async Task<IResult> GetActivityAsync(string? timeZone, WatchHistoryAdminService watchHistoryAdminService, CancellationToken cancellationToken = default)
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

    /// <summary>Adds a playback, started now, of a TMDB metadata entry to the watch history.</summary>
    public static async Task<IResult> PostAsync(int tmdbInfoId, WatchHistoryAdminService watchHistoryAdminService, IOptions<TmdbOptions> tmdbOptions, CancellationToken cancellationToken = default)
    {
        var item = await watchHistoryAdminService.AddAsync(tmdbInfoId, cancellationToken);

        return item is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(AdminWatchHistoryEntryDto.From(item, tmdbOptions.Value.ImageBaseUrl));
    }

    public static async Task<IResult> DeleteAsync(int id, WatchHistoryAdminService watchHistoryAdminService, CancellationToken cancellationToken = default)
    {
        var deleted = await watchHistoryAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
