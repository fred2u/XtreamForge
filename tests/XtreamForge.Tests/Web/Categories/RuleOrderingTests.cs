using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Tests.Web.Categories;

public class RuleOrderingTests
{
    // ─── NextSequence ───────────────────────────────────────────────────────

    [Fact]
    public void NextSequence_WhenNoRule_ReturnsStep()
    {
        Assert.Equal(RuleOrdering.SequenceStep, RuleOrdering.NextSequence([]));
    }

    [Fact]
    public void NextSequence_ReturnsHighestSequencePlusStep()
    {
        var rules = Rules(5, 42, 7);

        Assert.Equal(52, RuleOrdering.NextSequence(rules));
    }

    // ─── Move ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2, 0, new[] { 3, 1, 2 })]
    [InlineData(0, 2, new[] { 2, 3, 1 })]
    [InlineData(1, 0, new[] { 2, 1, 3 })]
    [InlineData(1, 1, new[] { 1, 2, 3 })]
    public void Move_ReturnsReorderedCopy(int oldIndex, int newIndex, int[] expectedIds)
    {
        var rules = Rules(10, 20, 30);

        var moved = RuleOrdering.Move(rules, oldIndex, newIndex);

        Assert.Equal(expectedIds, moved.Select(rule => rule.Id));
        Assert.Equal([1, 2, 3], rules.Select(rule => rule.Id));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    public void Move_WhenIndexIsOutOfRange_Throws(int oldIndex, int newIndex)
    {
        var rules = Rules(10, 20, 30);

        Assert.Throws<ArgumentOutOfRangeException>(() => RuleOrdering.Move(rules, oldIndex, newIndex));
    }

    // ─── HasSameOrder ───────────────────────────────────────────────────────

    [Fact]
    public void HasSameOrder_ComparesRuleIdsInOrder()
    {
        var rules = Rules(10, 20, 30);

        Assert.True(RuleOrdering.HasSameOrder(rules, [.. rules]));
        Assert.False(RuleOrdering.HasSameOrder(rules, RuleOrdering.Move(rules, 0, 1)));
    }

    // Rule ids are 1-based positions in the given order.
    private static List<RuleDto> Rules(params int[] sequences) =>
    [
        .. sequences.Select((sequence, index) => new RuleDto(
            index + 1,
            1,
            ContentType.Vod,
            sequence,
            RuleAction.Exclude,
            RuleOperator.Contains,
            $"pattern {index}",
            false,
            true))
    ];
}
