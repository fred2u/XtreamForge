using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;

namespace XtreamForge.ApiService.Services;

public class SourceService(XtreamForgeDbContext dbContext, TimeProvider timeProvider)
{
    /// <summary>
    /// Loads the data of the source matching the request destination for the requested content type; returns null when the source is unknown.
    /// </summary>
    public async Task<XtreamSourceSnapshot?> GetSnapshotAsync(XtreamContext xtreamContext, CancellationToken cancellationToken)
    {
        var source = await dbContext.XtreamSources
            .AsNoTracking()
            .Include(s => s.CategoryRules.Where(c => c.ContentType == xtreamContext.ContentType && c.IsEnabled))
            .Include(s => s.ItemRules.Where(c => c.ContentType == xtreamContext.ContentType && c.IsEnabled))
            .SingleOrDefaultAsync(s => s.Protocol == xtreamContext.Protocol && s.Host == xtreamContext.Host && s.Port == xtreamContext.Port, cancellationToken);

        if (source is null)
            return null;

        // only the lookup state of each stream is needed, and the mappings can be numerous
        var mappings = await dbContext.StreamTmdbMappings
            .AsNoTracking()
            .Where(mapping => mapping.XtreamSourceId == source.Id && mapping.ContentType == xtreamContext.ContentType)
            .Select(mapping => new { mapping.StreamId, mapping.TmdbId, mapping.NextLookupAtUtc })
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var streamTmdbMappings = new Dictionary<string, long>(StringComparer.Ordinal);
        var deferredTmdbLookups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            if (mapping.TmdbId is { } tmdbId)
                streamTmdbMappings[mapping.StreamId] = tmdbId;
            else if (mapping.NextLookupAtUtc > now)
                deferredTmdbLookups.Add(mapping.StreamId);
        }

        // the TMDB rules are global: they do not depend on the source
        var tmdbRules = await dbContext.TmdbRules
            .AsNoTracking()
            .Where(rule => rule.ContentType == xtreamContext.ContentType && rule.IsEnabled)
            .OrderBy(rule => rule.Sequence)
            .ToListAsync(cancellationToken);

        return new XtreamSourceSnapshot(
            source.Id,
            [.. source.CategoryRules.OrderBy(rule => rule.Sequence)],
            [.. source.ItemRules.OrderBy(rule => rule.Sequence)],
            streamTmdbMappings,
            deferredTmdbLookups,
            tmdbRules);
    }
}
