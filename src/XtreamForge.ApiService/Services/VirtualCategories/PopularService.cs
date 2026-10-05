using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Domain.Enums;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Services.VirtualCategories;

public class PopularService(TmdbClient tmdbClient, TmdbIdCache cache, IOptions<TmdbOptions> tmdbOptions, ILogger<PopularService> logger)
{
    /// <summary>Number of TMDB popular pages (20 titles each) read per content type.</summary>
    public const int PageCount = 5;

    /// <summary>
    /// Returns the TMDB IDs of the movies (VOD) or TV shows (series) currently popular on TMDB, from <see cref="TmdbIdCache"/> when available.
    /// Without <c>Tmdb:ApiKey</c> the set is empty; a TMDB failure is logged and gives no popular title, so that the catalogue is still returned.
    /// </summary>
    public Task<IReadOnlySet<long>> GetPopularTmdbIdsAsync(ContentType contentType, CancellationToken cancellationToken = default)
        => cache.GetOrComputeAsync(TmdbIdCache.PopularKey(contentType), token => ComputeAsync(contentType, token), cancellationToken);

    private async Task<IReadOnlySet<long>?> ComputeAsync(ContentType contentType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tmdbOptions.Value.ApiKey))
            return new HashSet<long>();

        try
        {
            var pages = await Task.WhenAll(Enumerable.Range(1, PageCount).Select(page => tmdbClient.GetPopularAsync(contentType, page, cancellationToken)));
            return pages.SelectMany(ids => ids).ToHashSet();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Unable to load the TMDB popular titles. ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));
            return null;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The TMDB popular titles timed out. ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));
            return null;
        }
    }
}
