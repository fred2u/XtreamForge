using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.Queues;
using XtreamForge.Database;
using XtreamForge.Domain.Items;

namespace XtreamForge.ApiService.Services.TmdbIdRetriever;

public class ProviderTmdbIdService(XtreamForgeDbContext dbContext, TimeProvider timeProvider) : IQueueProcessor<ProviderTmdbIdRequest>
{
    Task<bool> IQueueProcessor<ProviderTmdbIdRequest>.ProcessAsync(ProviderTmdbIdRequest request, CancellationToken cancellationToken)
        => SaveAsync(request, cancellationToken);

    /// <summary>
    /// Persists the provider TMDB IDs of list items, so that <c>get_vod_info</c> / <c>get_series_info</c> can use them when their payload has none.
    /// A stream without mapping is mapped, a mapping without TMDB ID (lookup without result) is completed and no longer looked up; an existing
    /// TMDB ID, possibly corrected by the admin, is never replaced. Returns true when a mapping was created or completed.
    /// </summary>
    public async Task<bool> SaveAsync(ProviderTmdbIdRequest request, CancellationToken cancellationToken)
    {
        var streamIds = request.TmdbIds.Keys.ToList();
        var mappings = await dbContext.StreamTmdbMappings
            .Where(mapping => mapping.XtreamSourceId == request.XtreamSourceId && mapping.ContentType == request.Type && streamIds.Contains(mapping.StreamId))
            .ToDictionaryAsync(mapping => mapping.StreamId, StringComparer.Ordinal, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var hasChanges = false;
        foreach (var (streamId, tmdbId) in request.TmdbIds)
        {
            if (!mappings.TryGetValue(streamId, out var mapping))
            {
                dbContext.StreamTmdbMappings.Add(new StreamTmdbMapping
                {
                    XtreamSourceId = request.XtreamSourceId,
                    ContentType = request.Type,
                    StreamId = streamId,
                    TmdbId = tmdbId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                hasChanges = true;
            }
            else if (mapping.TmdbId is null)
            {
                mapping.TmdbId = tmdbId;
                mapping.NextLookupAtUtc = null;
                mapping.UpdatedAtUtc = now;
                hasChanges = true;
            }
        }

        if (!hasChanges)
            return false;

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
