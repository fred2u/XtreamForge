using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb.Scoring;

/// <summary>
/// Genres: maximum 10 points, 5 (neutral) when the information is unavailable.
/// Candidate genres contain both the localized and the English TMDB names.
/// </summary>
public sealed class GenreScoringRule : ITmdbScoringRule
{
    public TmdbScoringStage Stage => TmdbScoringStage.Basic;

    public bool AppliesTo(ContentType type) => type is ContentType.Vod or ContentType.Series;

    public ValueTask<int> ScoreAsync(TmdbScoringContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(SetOverlapScore.Score(context.Source.Genres, context.Candidate.Genres));
}
