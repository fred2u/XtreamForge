using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;

namespace XtreamForge.ApiService.Services.Admin;

public class ItemRuleAdminService(XtreamForgeDbContext dbContext)
{
    /// <summary>Gap between the sequences assigned by <see cref="ReorderAsync"/>, which leaves room to add rules in between.</summary>
    public const int SequenceStep = 10;

    public async Task<IReadOnlyList<ItemRule>> GetBySourceAsync(
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ItemRules
            .AsNoTracking()
            .Where(r => r.XtreamSourceId == sourceId && r.ContentType == contentType)
            .OrderBy(r => r.Sequence)
            .ToListAsync(cancellationToken);
    }

    public async Task<(ItemRule? Rule, bool SequenceConflict)> CreateAsync(
        int sourceId,
        ContentType contentType,
        int sequence,
        RuleAction action,
        RuleOperator @operator,
        string pattern,
        bool caseSensitive,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var sourceExists = await dbContext.XtreamSources
            .AnyAsync(s => s.Id == sourceId, cancellationToken);

        if (!sourceExists)
        {
            return (null, false);
        }

        var sequenceConflict = await dbContext.ItemRules
            .AnyAsync(r => r.XtreamSourceId == sourceId && r.ContentType == contentType && r.Sequence == sequence, cancellationToken);

        if (sequenceConflict)
        {
            return (null, true);
        }

        var rule = new ItemRule
        {
            XtreamSourceId = sourceId,
            ContentType = contentType,
            Sequence = sequence,
            Action = action,
            Operator = @operator,
            Pattern = pattern,
            CaseSensitive = caseSensitive,
            IsEnabled = isEnabled
        };

        dbContext.ItemRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (rule, false);
    }

    public async Task<(bool Found, bool SequenceConflict)> UpdateAsync(
        int id,
        int sequence,
        RuleAction action,
        RuleOperator @operator,
        string pattern,
        bool caseSensitive,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var rule = await dbContext.ItemRules
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rule is null)
        {
            return (false, false);
        }

        var sequenceConflict = await dbContext.ItemRules
            .AnyAsync(r => r.XtreamSourceId == rule.XtreamSourceId
                        && r.ContentType == rule.ContentType
                        && r.Sequence == sequence
                        && r.Id != id, cancellationToken);

        if (sequenceConflict)
        {
            return (true, true);
        }

        rule.Sequence = sequence;
        rule.Action = action;
        rule.Operator = @operator;
        rule.Pattern = pattern;
        rule.CaseSensitive = caseSensitive;
        rule.IsEnabled = isEnabled;
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return (true, false);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rule = await dbContext.ItemRules
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rule is null)
        {
            return false;
        }

        dbContext.ItemRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Renumbers the rules of a source and content type in the given order (first = lowest sequence = highest priority),
    /// using sequences <see cref="SequenceStep"/>, 2 x <see cref="SequenceStep"/>, ...
    /// <paramref name="ruleIds"/> must contain every rule of the source and content type exactly once; otherwise nothing changes.
    /// All rules are updated in a single transaction.
    /// </summary>
    public async Task<(RuleReorderResult Result, IReadOnlyList<ItemRule> Rules)> ReorderAsync(
        int sourceId,
        ContentType contentType,
        IReadOnlyList<int> ruleIds,
        CancellationToken cancellationToken = default)
    {
        var sourceExists = await dbContext.XtreamSources
            .AnyAsync(s => s.Id == sourceId, cancellationToken);

        if (!sourceExists)
        {
            return (RuleReorderResult.SourceNotFound, []);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rules = await dbContext.ItemRules
            .Where(r => r.XtreamSourceId == sourceId && r.ContentType == contentType)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var requestedIds = ruleIds.ToHashSet();
        if (requestedIds.Count != ruleIds.Count || !requestedIds.SetEquals(rules.Keys))
        {
            return (RuleReorderResult.InvalidOrder, []);
        }

        // The unique (source, content type, sequence) index is checked for every statement, so the rules first move
        // to temporary negative sequences that no rule uses, then to their final positive sequence.
        var usedSequences = rules.Values.Select(r => r.Sequence).ToHashSet();
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
}
