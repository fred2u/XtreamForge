using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb.Scoring;

/// <summary>
/// Poster: 40 points when the provider reuses the TMDB poster, which is a very strong signal.
/// </summary>
public sealed class PosterScoringRule : ITmdbScoringRule
{
    private const int MinimumPosterIdLength = 7;

    public TmdbScoringStage Stage => TmdbScoringStage.Basic;

    public bool AppliesTo(ContentType type) => type is ContentType.Vod or ContentType.Series;

    public ValueTask<int> ScoreAsync(TmdbScoringContext context, CancellationToken cancellationToken)
    {
        var sourcePosterId = context.Source.PosterId;
        var candidatePosterPath = context.Candidate.PosterPath;

        if (sourcePosterId.Length < MinimumPosterIdLength || string.IsNullOrWhiteSpace(candidatePosterPath))
            return ValueTask.FromResult(0);

        var candidatePosterId = Path.GetFileNameWithoutExtension(candidatePosterPath);

        return ValueTask.FromResult(sourcePosterId.Equals(candidatePosterId, StringComparison.OrdinalIgnoreCase) ? 40 : 0);
    }
}
