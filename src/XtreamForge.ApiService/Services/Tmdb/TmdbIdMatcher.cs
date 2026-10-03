using Microsoft.Extensions.Options;
using System.Text.Json;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Tmdb.Scoring;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb;

/// <summary>
/// Searches TMDB candidates for a provider item and keeps the best scored one when it reaches the minimum confidence score.
/// </summary>
public class TmdbIdMatcher(TmdbClient tmdbClient, IEnumerable<ITmdbScoringRule> scoringRules, IOptions<TmdbOptions> options)
{
    private const int MaximumCandidateCount = 5;

    // advanced rules (additional TMDB calls) are only evaluated when the basic score is promising
    private const int AdvancedScoringMinimumScore = 60;

    public async Task<long?> FindTmdbIdAsync(ContentType type, JsonElement providerInfo, string? streamIcon, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
            return null;

        var source = TmdbSourceItemParser.Parse(type, providerInfo, streamIcon);
        if (!source.IsScorable)
        {
            return null;
        }

        var candidateIds = await tmdbClient.SearchAsync(type, source.Title, source.ReleaseDate?.Year, cancellationToken);
        if (candidateIds.Count is 0 or > MaximumCandidateCount)
        {
            return null;
        }

        var rules = scoringRules.Where(rule => rule.AppliesTo(type)).ToList();

        long? bestTmdbId = null;
        var bestScore = 0;

        foreach (var candidateId in candidateIds)
        {
            var candidate = await tmdbClient.GetCandidateAsync(type, candidateId, cancellationToken);
            if (candidate is null)
                continue;

            var score = await ScoreAsync(rules, new TmdbScoringContext(source, candidate, candidateIds.Count), cancellationToken);
            if (score > bestScore)
            {
                bestScore = score;
                bestTmdbId = candidate.Id;
            }
        }

        if (bestTmdbId is null || bestScore < options.Value.MinimumConfidenceScore)
        {
            return null;
        }

        return bestTmdbId;
    }

    private static async Task<int> ScoreAsync(List<ITmdbScoringRule> rules, TmdbScoringContext context, CancellationToken cancellationToken)
    {
        var score = await ScoreStageAsync(rules, TmdbScoringStage.Basic, context, cancellationToken);
        if (score < AdvancedScoringMinimumScore)
            return score;

        return score + await ScoreStageAsync(rules, TmdbScoringStage.Advanced, context, cancellationToken);
    }

    private static async Task<int> ScoreStageAsync(List<ITmdbScoringRule> rules, TmdbScoringStage stage, TmdbScoringContext context, CancellationToken cancellationToken)
    {
        var score = 0;
        foreach (var rule in rules.Where(rule => rule.Stage == stage))
        {
            score += await rule.ScoreAsync(context, cancellationToken);
        }

        return score;
    }
}
