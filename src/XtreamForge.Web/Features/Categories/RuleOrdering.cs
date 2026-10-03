namespace XtreamForge.Web.Features.Categories;

/// <summary>
/// List helpers of the category rules screen. Rules are evaluated by ascending <c>Sequence</c> (first match wins);
/// a new order is stored with the atomic reorder endpoint, which assigns the sequences itself.
/// </summary>
public static class RuleOrdering
{
    /// <summary>Same gap as the sequences assigned by the reorder endpoint.</summary>
    public const int SequenceStep = 10;

    /// <summary>Sequence for a new rule, placed after every existing rule (lowest priority).</summary>
    public static int NextSequence(IReadOnlyList<RuleDto> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.Count == 0 ? SequenceStep : rules.Max(rule => rule.Sequence) + SequenceStep;
    }

    /// <summary>Returns a copy of <paramref name="rules"/> with the rule at <paramref name="oldIndex"/> moved to <paramref name="newIndex"/>.</summary>
    public static List<RuleDto> Move(IReadOnlyList<RuleDto> rules, int oldIndex, int newIndex)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegative(oldIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(oldIndex, rules.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, rules.Count);

        var reordered = rules.ToList();
        var moved = reordered[oldIndex];
        reordered.RemoveAt(oldIndex);
        reordered.Insert(newIndex, moved);
        return reordered;
    }

    /// <summary>True when both lists contain the same rules in the same order.</summary>
    public static bool HasSameOrder(IReadOnlyList<RuleDto> rules, IReadOnlyList<RuleDto> other)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(other);
        return rules.Select(rule => rule.Id).SequenceEqual(other.Select(rule => rule.Id));
    }
}
