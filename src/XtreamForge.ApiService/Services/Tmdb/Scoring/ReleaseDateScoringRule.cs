using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb.Scoring;

/// <summary>
/// Release date: maximum 25 points, tolerating small differences between countries and providers.
/// </summary>
public sealed class ReleaseDateScoringRule : ITmdbScoringRule
{
    private const int MaximumScore = 25;
    private const int ToleratedDays = 15;
    private const int SameYearScore = 10;

    public TmdbScoringStage Stage => TmdbScoringStage.Basic;

    public bool AppliesTo(ContentType type) => type is ContentType.Vod or ContentType.Series;

    public ValueTask<int> ScoreAsync(TmdbScoringContext context, CancellationToken cancellationToken)
    {
        if (context.Source.ReleaseDate is not { } sourceDate || context.Candidate.ReleaseDate is not { } candidateDate)
            return ValueTask.FromResult(0);

        var delta = Math.Abs((sourceDate.Date - candidateDate.Date).Days);

        if (delta <= ToleratedDays)
            return ValueTask.FromResult(MaximumScore - delta);

        return ValueTask.FromResult(sourceDate.Year == candidateDate.Year ? SameYearScore : 0);
    }
}
