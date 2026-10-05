using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Services.Catalog;

/// <summary>
/// Read-only data of an Xtream source needed to process a catalogue request for one content type.
/// Rules are ordered by ascending sequence and only contain enabled rules; <see cref="TmdbRules"/> are the global TMDB rules of the content type.
/// <see cref="DeferredTmdbLookups"/> contains the streams without TMDB ID whose next lookup is not due yet.
/// </summary>
public sealed record XtreamSourceSnapshot(
    int Id,
    IReadOnlyList<CategoryRule> CategoryRules,
    IReadOnlyList<ItemRule> ItemRules,
    IReadOnlyDictionary<string, long> StreamTmdbMappings,
    IReadOnlySet<string> DeferredTmdbLookups,
    IReadOnlyList<TmdbRule> TmdbRules);
