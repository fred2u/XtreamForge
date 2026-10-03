namespace XtreamForge.ApiService.Endpoints.Admin.Dashboard.Dto;

/// <param name="UnresolvedTmdbMappingCount">Streams whose TMDB ID lookup ended without result or failed (mapping with a null TMDB ID).</param>
/// <param name="NotLoadedTmdbInfoCount">
/// TMDB metadata entries whose details have never been loaded: an entry is only stored by a load, so these are the entries whose first load failed
/// (unknown TMDB ID or error) and that wait for a retry; IDs waiting for their first load are only in the in-memory queue.
/// </param>
public sealed record DashboardStatusDto(
    string ApplicationName,
    string ApplicationVersion,
    string Status,
    string DatabaseStatus,
    string DatabaseDetails,
    int SourceCount,
    int SourceCategoryCount,
    int RuleCount,
    int CustomCategoryCount,
    int ItemRuleCount,
    int TmdbRuleCount,
    int KnownTmdbMappingCount,
    int UnresolvedTmdbMappingCount,
    int TmdbInfoCount,
    int NotLoadedTmdbInfoCount,
    int ManuallyExcludedTmdbInfoCount);
