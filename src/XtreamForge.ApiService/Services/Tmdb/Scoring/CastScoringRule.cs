using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb.Scoring;

/// <summary>
/// Cast: maximum 20 points, 5 (neutral) when the information is unavailable.
/// </summary>
public sealed class CastScoringRule : ITmdbScoringRule
{
    public TmdbScoringStage Stage => TmdbScoringStage.Basic;

    public bool AppliesTo(ContentType type) => type is ContentType.Vod or ContentType.Series;

    public ValueTask<int> ScoreAsync(TmdbScoringContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(SetOverlapScore.Score(context.Source.Cast, context.Candidate.Cast, maximumScore: 20));
}
