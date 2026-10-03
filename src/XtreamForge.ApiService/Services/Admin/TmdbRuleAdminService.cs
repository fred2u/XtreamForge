using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Services.Admin;

/// <summary>Values of a TMDB rule set by the administrator.</summary>
public sealed record TmdbRuleValues(
    int Sequence,
    TmdbRuleField Field,
    RuleAction Action,
    RuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);

/// <summary>Administration of the TMDB rules, which are global per content type (same contracts as the item rules, without source).</summary>
public class TmdbRuleAdminService(XtreamForgeDbContext dbContext)
{
    /// <summary>Gap between the sequences assigned by <see cref="ReorderAsync"/>, which leaves room to add rules in between.</summary>
    public const int SequenceStep = 10;

    public async Task<IReadOnlyList<TmdbRule>> GetAsync(ContentType contentType, CancellationToken cancellationToken = default)
    {
        return await dbContext.TmdbRules
            .AsNoTracking()
            .Where(rule => rule.ContentType == contentType)
            .OrderBy(rule => rule.Sequence)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns null with <c>SequenceConflict</c> when another rule of the content type already uses the sequence.</summary>
    public async Task<(TmdbRule? Rule, bool SequenceConflict)> CreateAsync(ContentType contentType, TmdbRuleValues values, CancellationToken cancellationToken = default)
    {
        var sequenceConflict = await dbContext.TmdbRules
            .AnyAsync(rule => rule.ContentType == contentType && rule.Sequence == values.Sequence, cancellationToken);

        if (sequenceConflict)
        {
            return (null, true);
        }

        var rule = new TmdbRule { ContentType = contentType, Pattern = values.Pattern };
        Apply(rule, values);

        dbContext.TmdbRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (rule, false);
    }

    /// <summary>Updates a rule; its content type cannot change.</summary>
    public async Task<(bool Found, bool SequenceConflict)> UpdateAsync(int id, TmdbRuleValues values, CancellationToken cancellationToken = default)
    {
        var rule = await dbContext.TmdbRules
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

        if (rule is null)
        {
            return (false, false);
        }

        var sequenceConflict = await dbContext.TmdbRules
            .AnyAsync(other => other.ContentType == rule.ContentType && other.Sequence == values.Sequence && other.Id != id, cancellationToken);

        if (sequenceConflict)
        {
            return (true, true);
        }

        Apply(rule, values);
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
    /// Renumbers the rules of a content type in the given order (first = lowest sequence = highest priority),
    /// using sequences <see cref="SequenceStep"/>, 2 x <see cref="SequenceStep"/>, ...
    /// <paramref name="ruleIds"/> must contain every rule of the content type exactly once; otherwise nothing changes.
    /// All rules are updated in a single transaction.
    /// </summary>
    public async Task<(RuleReorderResult Result, IReadOnlyList<TmdbRule> Rules)> ReorderAsync(
        ContentType contentType,
        IReadOnlyList<int> ruleIds,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rules = await dbContext.TmdbRules
            .Where(rule => rule.ContentType == contentType)
            .ToDictionaryAsync(rule => rule.Id, cancellationToken);

        var requestedIds = ruleIds.ToHashSet();
        if (requestedIds.Count != ruleIds.Count || !requestedIds.SetEquals(rules.Keys))
        {
            return (RuleReorderResult.InvalidOrder, []);
        }

        // The unique (content type, sequence) index is checked for every statement, so the rules first move
        // to temporary negative sequences that no rule uses, then to their final positive sequence.
        var usedSequences = rules.Values.Select(rule => rule.Sequence).ToHashSet();
        var temporarySequence = 0;
        foreach (var rule in rules.Values)
        {
            do
            {
                temporarySequence--;
            }
            while (usedSequences.Contains(temporarySequence));

            rule.Sequence = temporarySequence;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < ruleIds.Count; index++)
        {
            var rule = rules[ruleIds[index]];
            rule.Sequence = (index + 1) * SequenceStep;
            rule.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (RuleReorderResult.Reordered, [.. ruleIds.Select(id => rules[id])]);
    }

    private static void Apply(TmdbRule rule, TmdbRuleValues values)
    {
        rule.Sequence = values.Sequence;
        rule.Field = values.Field;
        rule.Action = values.Action;
        rule.Operator = values.Operator;
        rule.Pattern = values.Pattern;
        rule.CaseSensitive = values.CaseSensitive;
        rule.IsEnabled = values.IsEnabled;
    }
}
