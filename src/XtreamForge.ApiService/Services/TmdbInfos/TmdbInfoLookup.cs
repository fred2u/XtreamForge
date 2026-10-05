using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Services.TmdbInfos;

/// <summary>
/// TMDB metadata of a batch of TMDB IDs: the entries that are not excluded manually, and only the IDs of the excluded ones
/// (their metadata is not needed, the items are excluded). An ID in neither collection has no metadata yet.
/// </summary>
public sealed record TmdbInfoLookup(IReadOnlyDictionary<long, TmdbInfo> Infos, IReadOnlySet<long> ExcludedTmdbIds)
{
    public static readonly TmdbInfoLookup Empty = new(new Dictionary<long, TmdbInfo>(), new HashSet<long>());
}
