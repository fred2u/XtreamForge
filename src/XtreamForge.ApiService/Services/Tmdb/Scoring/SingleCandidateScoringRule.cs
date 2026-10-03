using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb.Scoring;

/// <summary>
/// A single search result is a positive signal (10 points), but never enough on its own.
/// </summary>
public sealed class SingleCandidateScoringRule : ITmdbScoringRule
{
    public TmdbScoringStage Stage => TmdbScoringStage.Basic;

    public bool AppliesTo(ContentType type) => type is ContentType.Vod or ContentType.Series;

    public ValueTask<int> ScoreAsync(TmdbScoringContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(context.CandidateCount == 1 ? 10 : 0);
}
