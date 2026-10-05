using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;

namespace XtreamForge.ApiService.Services.Admin;

public class SourceAdminService(XtreamForgeDbContext dbContext)
{
    public async Task<IReadOnlyList<XtreamSource>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.XtreamSources
            .AsNoTracking()
            .OrderBy(s => s.Host)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns every source with its counters per content type. The effective categories are the ones the
    /// category filtering sends to clients (manual exclusion, provider state and rules), evaluated in memory
    /// with <see cref="CategoryRuleService"/> so the counts always match the Xtream categories screen.
    /// </summary>
    public async Task<IReadOnlyList<SourceSummary>> GetSummariesAsync(CancellationToken cancellationToken = default)
    {
        var sources = await GetAllAsync(cancellationToken);

        var categories = (await dbContext.XtreamCategories
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .ToLookup(c => (c.XtreamSourceId, c.ContentType));
        var categoryRules = (await dbContext.CategoryRules
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .ToLookup(r => (r.XtreamSourceId, r.ContentType));
        var itemRuleCounts = (await dbContext.ItemRules
            .AsNoTracking()
            .GroupBy(r => new { r.XtreamSourceId, r.ContentType })
            .Select(g => new { g.Key.XtreamSourceId, g.Key.ContentType, Count = g.Count() })
            .ToListAsync(cancellationToken))
            .ToDictionary(g => (g.XtreamSourceId, g.ContentType), g => g.Count);
        var tmdbMappingCounts = (await dbContext.StreamTmdbMappings
            .AsNoTracking()
            .Where(m => m.TmdbId != null)
            .GroupBy(m => new { m.XtreamSourceId, m.ContentType })
            .Select(g => new { g.Key.XtreamSourceId, g.Key.ContentType, Count = g.Count() })
            .ToListAsync(cancellationToken))
            .ToDictionary(g => (g.XtreamSourceId, g.ContentType), g => g.Count);

        SourceContentSummary Summarize(int sourceId, ContentType contentType)
        {
            var key = (sourceId, contentType);
            var rules = categoryRules[key].ToList();
            var evaluations = RuleEvaluator.EvaluateCategories(categories[key], rules).ToList();

            return new SourceContentSummary(
                evaluations.Count,
                evaluations.Count(e => e.Decision == InclusionDecision.Include),
                evaluations.Count(e => e.ExclusionReason == CategoryExclusionReason.ManuallyExcluded),
                evaluations.Count(e => e.ExclusionReason == CategoryExclusionReason.ProviderDisabled),
                evaluations.Count(e => e.ExclusionReason == CategoryExclusionReason.Rule),
                evaluations.Count(e => e.Category.CustomCategoryId is not null),
                rules.Count,
                itemRuleCounts.GetValueOrDefault(key),
                tmdbMappingCounts.GetValueOrDefault(key));
        }

        return [.. sources.Select(s => new SourceSummary(
            s,
            Summarize(s.Id, ContentType.Vod),
            Summarize(s.Id, ContentType.Series)))];
    }
    public async Task<XtreamSource?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        return await dbContext.XtreamSources
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string protocol, string host, int port, CancellationToken cancellationToken = default)
    {
        return await dbContext.XtreamSources
            .AnyAsync(s => s.Protocol == protocol && s.Host == host && s.Port == port, cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var source = await dbContext.XtreamSources
            .FindAsync([id], cancellationToken);

        if (source is null)
        {
            return false;
        }

        dbContext.XtreamSources.Remove(source);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
