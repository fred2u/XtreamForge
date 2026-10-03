namespace XtreamForge.Domain.Enums;

/// <summary>Why a movie or TV show is excluded by its TMDB metadata, in the order the checks are made.</summary>
public enum TmdbExclusionReason
{
    ManuallyExcluded = 1,
    Rule = 2
}
