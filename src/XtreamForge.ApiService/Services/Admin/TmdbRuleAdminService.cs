using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Services.Admin;

/// <summary>
/// Administration of the TMDB rules, which are global per content type (same contracts as the source rules, without source,
/// with the TMDB <see cref="TmdbRuleField"/> the rule matches).
/// </summary>
public class TmdbRuleAdminService(XtreamForgeDbContext dbContext)
{
    public async Task<IReadOnlyList<TmdbRule>> GetAsync(ContentType contentType, CancellationToken cancellationToken = default)
    {
        return await InScope(contentType)
            .AsNoTracking()
            .OrderBy(rule => rule.Sequence)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns null with <c>SequenceConflict</c> when another rule of the content type already uses the sequence.</summary>
    public async Task<(TmdbRule? Rule, bool SequenceConflict)> CreateAsync(
        ContentType contentType,
        TmdbRuleField field,
        RuleValues values,
        CancellationToken cancellationToken = default)
    {
        var sequenceConflict = await InScope(contentType)
            .AnyAsync(rule => rule.Sequence == values.Sequence, cancellationToken);

        if (sequenceConflict)
        {
            return (null, true);
        }

        var rule = new TmdbRule { ContentType = contentType, Field = field, Pattern = values.Pattern };
        values.ApplyTo(rule);

        dbContext.TmdbRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (rule, false);
    }

    /// <summary>Updates a rule; its content type cannot change.</summary>
    public async Task<(bool Found, bool SequenceConflict)> UpdateAsync(
        int id,
        TmdbRuleField field,
        RuleValues values,
        CancellationToken cancellationToken = default)
    {
        var rule = await dbContext.TmdbRules
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

        if (rule is null)
        {
            return (false, false);
        }

        var sequenceConflict = await InScope(rule.ContentType)
            .AnyAsync(other => other.Sequence == values.Sequence && other.Id != id, cancellationToken);

        if (sequenceConflict)
        {
            return (true, true);
        }

        rule.Field = field;
        values.ApplyTo(rule);
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return (true, false);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rule = await dbContext.TmdbRules
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

        if (rule is null)
        {
            return false;
        }

        dbContext.TmdbRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Renumbers the rules of a content type in the given order (see <see cref="RuleSequences.ReorderAsync"/>);
    /// <paramref name="ruleIds"/> must contain every rule of the content type exactly once, otherwise nothing changes.
    /// </summary>
    public async Task<(RuleReorderResult Result, IReadOnlyList<TmdbRule> Rules)> ReorderAsync(
        ContentType contentType,
        IReadOnlyList<int> ruleIds,
        CancellationToken cancellationToken = default)
    {
        var rules = await RuleSequences.ReorderAsync(dbContext, InScope(contentType), ruleIds, cancellationToken);

        return rules is null
            ? (RuleReorderResult.InvalidOrder, [])
            : (RuleReorderResult.Reordered, rules);
    }

    private IQueryable<TmdbRule> InScope(ContentType contentType) =>
        dbContext.TmdbRules.Where(rule => rule.ContentType == contentType);
}
