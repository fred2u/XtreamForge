using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.WatchHistory.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.WatchHistory;

public class WatchHistoryGetEndpoint(WatchHistoryAdminService watchHistoryAdminService, IOptions<TmdbOptions> tmdbOptions)
{
    public async Task<IResult> GetAsync(WatchHistoryListQuery query, CancellationToken cancellationToken = default)
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
}
