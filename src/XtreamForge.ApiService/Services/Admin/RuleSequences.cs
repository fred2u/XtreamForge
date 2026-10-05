using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;
using XtreamForge.Domain.Rules;

namespace XtreamForge.ApiService.Services.Admin;

/// <summary>Renumbering of the rules of a scope, shared by the category, item, and TMDB rules.</summary>
public static class RuleSequences
{
    /// <summary>Gap between the sequences assigned by <see cref="ReorderAsync"/>, which leaves room to add rules in between.</summary>
    public const int Step = 10;

    /// <summary>
    /// Renumbers the rules of <paramref name="scope"/> in the given order (first = lowest sequence = highest priority),
    /// using sequences <see cref="Step"/>, 2 x <see cref="Step"/>, ... in a single transaction.
    /// Returns null, and changes nothing, when <paramref name="ruleIds"/> does not contain every rule of the scope exactly once.
    /// </summary>
    public static async Task<IReadOnlyList<TRule>?> ReorderAsync<TRule>(
        XtreamForgeDbContext dbContext,
        IQueryable<TRule> scope,
        IReadOnlyList<int> ruleIds,
        CancellationToken cancellationToken = default)
        where TRule : class, IRule
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rules = await scope.ToDictionaryAsync(rule => rule.Id, cancellationToken);

        var requestedIds = ruleIds.ToHashSet();
        if (requestedIds.Count != ruleIds.Count || !requestedIds.SetEquals(rules.Keys))
        {
            return null;
        }

        // The unique (scope, sequence) index is checked for every statement, so the rules first move
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
            rule.Sequence = (index + 1) * Step;
            rule.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return [.. ruleIds.Select(id => rules[id])];
    }
}
