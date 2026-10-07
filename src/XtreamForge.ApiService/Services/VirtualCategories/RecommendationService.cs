using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Services.VirtualCategories;

/// <summary>
/// Movie or TV show recommended from the watch history. <see cref="Score"/> sums the weight of the watched movies or TV shows recommending it
/// (the most recently watched weighs the most), <see cref="RecommendedByCount"/> counts them, and
/// <see cref="IsInCatalogue"/> tells whether TMDB metadata is known for it, i.e. whether a source exposes it.
/// </summary>
public sealed record RecommendationItem(
    ContentType ContentType,
    long TmdbId,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterPath,
    double? VoteAverage,
    int? VoteCount,
    IReadOnlyList<string> Genres,
    int Score,
    int RecommendedByCount,
    bool IsInCatalogue);

public class RecommendationService(
    XtreamForgeDbContext dbContext,
    TmdbClient tmdbClient,
    TmdbIdCache cache,
    IOptions<TmdbOptions> tmdbOptions,
    ILogger<RecommendationService> logger)
{
    /// <summary>Number of the most recently watched movies or TV shows whose TMDB recommendations are requested.</summary>
    public const int SeedCount = 20;

    public const int MaximumCount = 50;

    /// <summary>
    /// Returns the TMDB IDs of the recommended movies (VOD) or TV shows (series) for the Xtream requests, from <see cref="TmdbIdCache"/> when available.
    /// A TMDB failure is logged and gives no recommendation, so that the catalogue is still returned.
    /// </summary>
    public Task<IReadOnlySet<long>> GetRecommendedTmdbIdsAsync(ContentType contentType, CancellationToken cancellationToken = default)
        => cache.GetOrComputeAsync(TmdbIdCache.RecommendationsKey(contentType), token => ComputeRecommendedTmdbIdsAsync(contentType, token), cancellationToken);

    /// <summary>
    /// Returns the movies (VOD) or TV shows (series) recommended by TMDB for the most recently watched movies or TV shows (episodes count
    /// for their series), the best score first. A movie or TV show of the watch history, or excluded manually from the TMDB metadata,
    /// is never recommended. Without <c>Tmdb:ApiKey</c>, TMDB is not called and the list is empty.
    /// </summary>
    public async Task<IReadOnlyList<RecommendationItem>> GetAsync(ContentType contentType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tmdbOptions.Value.ApiKey))
        {
            return [];
        }

        // every watched title with its last playback; the playbacks are recorded in their start order
        var watchedTitles = await dbContext.WatchHistory
            .AsNoTracking()
            .Where(entry => entry.ContentType == contentType)
            .GroupBy(entry => entry.TmdbId)
            .Select(group => new { TmdbId = group.Key, LastId = group.Max(entry => entry.Id) })
            .ToListAsync(cancellationToken);

        var seeds = watchedTitles
            .OrderByDescending(title => title.LastId)
            .Take(SeedCount)
            .Select(title => title.TmdbId)
            .ToList();

        var recommendationsBySeed = await Task.WhenAll(seeds.Select(seed => tmdbClient.GetRecommendationsAsync(contentType, seed, cancellationToken)));

        var watchedIds = watchedTitles.Select(title => title.TmdbId).ToHashSet();
        var candidates = new Dictionary<long, (TmdbRecommendation Recommendation, int Score, int Count)>();
        for (var seedIndex = 0; seedIndex < recommendationsBySeed.Length; seedIndex++)
        {
            var weight = SeedCount - seedIndex;
            foreach (var recommendation in recommendationsBySeed[seedIndex].Where(recommendation => !watchedIds.Contains(recommendation.Id)))
            {
                candidates[recommendation.Id] = candidates.TryGetValue(recommendation.Id, out var candidate)
                    ? (candidate.Recommendation, candidate.Score + weight, candidate.Count + 1)
                    : (recommendation, weight, 1);
            }
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var candidateIds = candidates.Keys.ToList();
        var knownInfos = await dbContext.TmdbInfos
            .AsNoTracking()
            .Where(info => info.ContentType == contentType && candidateIds.Contains(info.TmdbId))
            .Select(info => new { info.TmdbId, info.IsExcluded })
            .ToDictionaryAsync(info => info.TmdbId, info => info.IsExcluded, cancellationToken);

        return [.. candidates.Values
            .Where(candidate => !knownInfos.GetValueOrDefault(candidate.Recommendation.Id))
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Recommendation.VoteAverage ?? 0)
            .ThenBy(candidate => candidate.Recommendation.Id)
            .Take(MaximumCount)
            .Select(candidate => new RecommendationItem(
                contentType,
                candidate.Recommendation.Id,
                candidate.Recommendation.Title,
                candidate.Recommendation.OriginalTitle,
                candidate.Recommendation.ReleaseDate,
                candidate.Recommendation.PosterPath,
                candidate.Recommendation.VoteAverage,
                candidate.Recommendation.VoteCount,
                candidate.Recommendation.Genres,
                candidate.Score,
                candidate.Count,
                knownInfos.ContainsKey(candidate.Recommendation.Id)))];
    }

    private async Task<IReadOnlySet<long>?> ComputeRecommendedTmdbIdsAsync(ContentType contentType, CancellationToken cancellationToken)
    {
        try
        {
            var recommendations = await GetAsync(contentType, cancellationToken);
            return recommendations.Select(recommendation => recommendation.TmdbId).ToHashSet();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Unable to load the TMDB recommendations. ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));
            return null;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The TMDB recommendations timed out. ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));
            return null;
        }
    }
}
