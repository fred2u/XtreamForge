using XtreamForge.Items;

namespace XtreamForge.Tests;

public sealed class ItemRuleEvaluatorTests
{
    private readonly ItemRuleEvaluator _evaluator = new();

    [Fact]
    public void StartsWith_CaseSensitive_MatchesExactCase()
    {
        var result = Evaluate("|XXX| MOVIE", ItemRuleOperator.StartsWith, "|XXX|", true, ItemRuleAction.Exclude);
        Assert.Equal(ItemInclusionDecision.Exclude, result.Decision);
    }

    [Fact]
    public void StartsWith_CaseInsensitive_AcceptsDifferentCase()
    {
        var result = Evaluate("|xxx| movie", ItemRuleOperator.StartsWith, "|XXX|", false, ItemRuleAction.Exclude);
        Assert.Equal(ItemInclusionDecision.Exclude, result.Decision);
    }

    [Fact]
    public void Contains_CaseSensitive_RejectsDifferentCase()
    {
        var result = Evaluate("Some Movie vostfr", ItemRuleOperator.Contains, "VOST", true, ItemRuleAction.Exclude);
        Assert.Equal(ItemInclusionDecision.Include, result.Decision);
    }

    [Fact]
    public void FirstMatchingRule_Wins()
    {
        var result = _evaluator.Evaluate(
            new ItemRuleInput("SPORT DOCUMENTARY"),
            [
                new ItemRuleDefinition(1, 10, ItemRuleField.Name, ItemRuleAction.Include, ItemRuleOperator.Contains, "DOCUMENT", false, true),
                new ItemRuleDefinition(2, 20, ItemRuleField.Name, ItemRuleAction.Exclude, ItemRuleOperator.Contains, "SPORT", false, true)
            ]);

        Assert.Equal(ItemInclusionDecision.Include, result.Decision);
        Assert.Equal(10, result.MatchedRuleSequence);
    }

    [Fact]
    public void NoMatchingRule_DefaultsToInclude()
    {
        var result = _evaluator.Evaluate(
            new ItemRuleInput("Plain Movie"),
            [
                new ItemRuleDefinition(1, 10, ItemRuleField.Name, ItemRuleAction.Exclude, ItemRuleOperator.Contains, "SPORT", false, true)
            ]);

        Assert.Equal(ItemInclusionDecision.Include, result.Decision);
        Assert.False(result.IsMatch);
    }

    private ItemRuleEvaluationResult Evaluate(string itemName, ItemRuleOperator @operator, string pattern, bool caseSensitive, ItemRuleAction action) =>
        _evaluator.Evaluate(
            new ItemRuleInput(itemName),
            [
                new ItemRuleDefinition(1, 10, ItemRuleField.Name, action, @operator, pattern, caseSensitive, true)
            ]);
}
