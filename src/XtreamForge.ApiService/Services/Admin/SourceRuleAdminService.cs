using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Rules;

namespace XtreamForge.ApiService.Services.Admin;

/// <summary>
/// Administration of the rules defined per source: the category rules and the item rules. The sequence of a rule is unique
/// among the rules of its source and content type, which cannot change after its creation.
/// </summary>
public class SourceRuleAdminService<TRule>(XtreamForgeDbContext dbContext) where TRule : class, ISourceRule, new()
{
    public async Task<IReadOnlyList<TRule>> GetBySourceAsync(
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        return await InScope(sourceId, contentType)
            .AsNoTracking()
            .OrderBy(rule => rule.Sequence)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns null without <c>SequenceConflict</c> when the source does not exist,
    /// and null with <c>SequenceConflict</c> when another rule of the source and content type already uses the sequence.
    /// </summary>
    public async Task<(TRule? Rule, bool SequenceConflict)> CreateAsync(
        int sourceId,
        ContentType contentType,
        RuleValues values,
        CancellationToken cancellationToken = default)
    {
        if (!await SourceExistsAsync(sourceId, cancellationToken))
        {
            return (null, false);
        }

        var sequenceConflict = await InScope(sourceId, contentType)
            .AnyAsync(rule => rule.Sequence == values.Sequence, cancellationToken);

        if (sequenceConflict)
        {
            return (null, true);
        }

        var rule = new TRule { XtreamSourceId = sourceId, ContentType = contentType };
        values.ApplyTo(rule);

        dbContext.Set<TRule>().Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (rule, false);
    }

    public async Task<(bool Found, bool SequenceConflict)> UpdateAsync(int id, RuleValues values, CancellationToken cancellationToken = default)
    {
        var rule = await dbContext.Set<TRule>()
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

        if (rule is null)
        {
            return (false, false);
        }

        var sequenceConflict = await InScope(rule.XtreamSourceId, rule.ContentType)
            .AnyAsync(other => other.Sequence == values.Sequence && other.Id != id, cancellationToken);

        if (sequenceConflict)
        {
            return (true, true);
        }

        values.ApplyTo(rule);
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return (true, false);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rule = await dbContext.Set<TRule>()
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

        if (rule is null)
        {
            return false;
        }

        dbContext.Set<TRule>().Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Renumbers the rules of a source and content type in the given order (see <see cref="RuleSequences.ReorderAsync"/>);
    /// <paramref name="ruleIds"/> must contain every rule of the source and content type exactly once, otherwise nothing changes.
    /// </summary>
    public async Task<(RuleReorderResult Result, IReadOnlyList<TRule> Rules)> ReorderAsync(
        int sourceId,
        ContentType contentType,
        IReadOnlyList<int> ruleIds,
        CancellationToken cancellationToken = default)
    {
        if (!await SourceExistsAsync(sourceId, cancellationToken))
        {
            return (RuleReorderResult.SourceNotFound, []);
        }

        var rules = await RuleSequences.ReorderAsync(dbContext, InScope(sourceId, contentType), ruleIds, cancellationToken);

        return rules is null
            ? (RuleReorderResult.InvalidOrder, [])
            : (RuleReorderResult.Reordered, rules);
    }

    private IQueryable<TRule> InScope(int sourceId, ContentType contentType) =>
        dbContext.Set<TRule>().Where(rule => rule.XtreamSourceId == sourceId && rule.ContentType == contentType);

    private Task<bool> SourceExistsAsync(int sourceId, CancellationToken cancellationToken) =>
        dbContext.XtreamSources.AnyAsync(source => source.Id == sourceId, cancellationToken);
}
