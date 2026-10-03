using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb.Scoring;

public enum TmdbScoringStage
{
    /// <summary>
    /// Cheap rules evaluated for every candidate.
    /// </summary>
    Basic,

    /// <summary>
    /// Costly rules (for example additional TMDB calls) evaluated only when the basic score is promising.
    /// </summary>
    Advanced
}

public sealed record TmdbScoringContext(TmdbSourceItem Source, TmdbCandidate Candidate, int CandidateCount);

/// <summary>
/// A scoring rule adds points to a TMDB candidate. Add a new rule by implementing this interface and registering it in DI.
/// </summary>
public interface ITmdbScoringRule
{
    TmdbScoringStage Stage { get; }

    bool AppliesTo(ContentType type);

    ValueTask<int> ScoreAsync(TmdbScoringContext context, CancellationToken cancellationToken);
}
