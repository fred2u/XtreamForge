namespace XtreamForge.ApiService.Services.Tmdb.Scoring;

internal static class SetOverlapScore
{
    /// <summary>
    /// Proportion of source values found in the candidate, scaled to 10 points; 5 when either side is unknown.
    /// </summary>
    public static int Score(IReadOnlySet<string> sourceValues, IReadOnlySet<string> candidateValues, int maximumScore = 10, int neutralScore = 5)
    {
        if (sourceValues.Count == 0 || candidateValues.Count == 0)
            return neutralScore;

        var ratio = (double)sourceValues.Count(candidateValues.Contains) / sourceValues.Count;

        return (int)Math.Round(ratio * maximumScore);
    }
}
