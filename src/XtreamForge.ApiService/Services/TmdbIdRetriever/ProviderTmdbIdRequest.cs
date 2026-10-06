using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.TmdbIdRetriever;

/// <summary>
/// Provider TMDB IDs of a batch of list items without TMDB mapping, keyed by stream ID, to persist in the background.
/// </summary>
public sealed record ProviderTmdbIdRequest(int XtreamSourceId, ContentType Type, IReadOnlyDictionary<string, long> TmdbIds)
{
    // the compiler-generated ToString would only print the type of the dictionary
    public override string ToString() => $"{nameof(ProviderTmdbIdRequest)} {{ XtreamSourceId = {XtreamSourceId}, Type = {Type}, Count = {TmdbIds.Count} }}";
}
