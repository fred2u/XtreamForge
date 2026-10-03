using System.Text.RegularExpressions;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb.Scoring;

/// <summary>
/// Title: maximum 35 points.
/// </summary>
public sealed partial class TitleScoringRule : ITmdbScoringRule
{
    // "Title (Alternative title)"
    [GeneratedRegex(@"^(.*?)\s*\(([^()]*)\)$")]
    private static partial Regex TitleWithAlternative();

    public TmdbScoringStage Stage => TmdbScoringStage.Basic;

    public bool AppliesTo(ContentType type) => type is ContentType.Vod or ContentType.Series;

    public ValueTask<int> ScoreAsync(TmdbScoringContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(Score(context.Source.Title, context.Candidate.Title, context.Candidate.OriginalTitle));

    private static int Score(string rawSourceTitle, string rawCandidateTitle, string rawCandidateOriginalTitle)
    {
        var sourceTitle = TmdbText.Normalize(rawSourceTitle);
        var title = TmdbText.Normalize(rawCandidateTitle);
        var originalTitle = TmdbText.Normalize(rawCandidateOriginalTitle);
        string[] candidateTitles = [title, originalTitle, $"{title} {originalTitle}", $"{originalTitle} {title}"];

        if (candidateTitles.Contains(sourceTitle, StringComparer.Ordinal))
            return 35;

        var alternative = TitleWithAlternative().Match(rawSourceTitle);
        if (alternative.Success)
        {
            var firstTitle = TmdbText.Normalize(alternative.Groups[1].Value);
            var secondTitle = TmdbText.Normalize(alternative.Groups[2].Value);

            return IsAnyEqual(firstTitle, title, originalTitle) || IsAnyEqual(secondTitle, title, originalTitle) ? 30 : 0;
        }

        if (ContainsLongTitle(sourceTitle, title) || ContainsLongTitle(sourceTitle, originalTitle)
            || ContainsLongTitle(title, sourceTitle) || ContainsLongTitle(originalTitle, sourceTitle))
            return 20;

        var similarity = candidateTitles.Max(candidateTitle => TmdbText.Similarity(sourceTitle, candidateTitle));

        return similarity switch
        {
            >= 0.90 => 30,
            >= 0.80 => 20,
            >= 0.70 => 10,
            _ => 0
        };
    }

    private static bool IsAnyEqual(string value, string title, string originalTitle)
        => value.Length > 0 && (value == title || value == originalTitle);

    // containment is only trusted with long titles to avoid matching generic words
    private static bool ContainsLongTitle(string container, string contained)
        => container.Length > 15 && contained.Length > 10 && container.Contains(contained, StringComparison.Ordinal);
}
