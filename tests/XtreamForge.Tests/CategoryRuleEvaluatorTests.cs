using XtreamForge.Categories;

namespace XtreamForge.Tests;

public sealed class CategoryRuleEvaluatorTests
{
    private readonly CategoryRuleEvaluator _evaluator = new();

    [Fact]
    public void StartsWith_CaseSensitive_MatchesExactCase()
    {
        var result = Evaluate("|XXX| MOVIES", CategoryRuleOperator.StartsWith, "|XXX|", true, CategoryRuleAction.Exclude);
        Assert.Equal(CategoryInclusionDecision.Exclude, result.Decision);
    }

    [Fact]
    public void StartsWith_CaseSensitive_RejectsDifferentCase()
    {
        var result = Evaluate("|xxx| MOVIES", CategoryRuleOperator.StartsWith, "|XXX|", true, CategoryRuleAction.Exclude);
        Assert.Equal(CategoryInclusionDecision.Include, result.Decision);
    }

    [Fact]
    public void StartsWith_CaseInsensitive_AcceptsDifferentCase()
    {
        var result = Evaluate("|xxx| Movies", CategoryRuleOperator.StartsWith, "|XXX|", false, CategoryRuleAction.Exclude);
        Assert.Equal(CategoryInclusionDecision.Exclude, result.Decision);
    }

    [Fact]
    public void Contains_CaseSensitive_MatchesExactCase()
    {
        var result = Evaluate("|FR| SPORT", CategoryRuleOperator.Contains, "SPORT", true, CategoryRuleAction.Exclude);
        Assert.Equal(CategoryInclusionDecision.Exclude, result.Decision);
    }

    [Fact]
    public void Contains_CaseSensitive_RejectsDifferentCase()
    {
        var result = Evaluate("|FR| Sport", CategoryRuleOperator.Contains, "SPORT", true, CategoryRuleAction.Exclude);
        Assert.Equal(CategoryInclusionDecision.Include, result.Decision);
    }

    [Fact]
    public void Contains_CaseInsensitive_AcceptsDifferentCase()
    {
        var result = Evaluate("|FR| sport", CategoryRuleOperator.Contains, "SPORT", false, CategoryRuleAction.Exclude);
        Assert.Equal(CategoryInclusionDecision.Exclude, result.Decision);
    }

    [Fact]
    public void FirstIncludeMatch_StopsEvaluation()
    {
        var result = _evaluator.Evaluate("|FR| DOCUMENTAIRE SPORT",
        [
            new CategoryRuleDefinition(1, 10, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true),
            new CategoryRuleDefinition(2, 20, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true)
        ]);

        Assert.Equal(CategoryInclusionDecision.Include, result.Decision);
        Assert.Equal(10, result.MatchedRuleSequence);
    }

    [Fact]
    public void FirstExcludeMatch_StopsEvaluation()
    {
        var result = _evaluator.Evaluate("|FR| SPORT DOCUMENTAIRE",
        [
            new CategoryRuleDefinition(1, 10, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true),
            new CategoryRuleDefinition(2, 20, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true)
        ]);

        Assert.Equal(CategoryInclusionDecision.Exclude, result.Decision);
        Assert.Equal(10, result.MatchedRuleSequence);
    }

    [Fact]
    public void LaterExclude_DoesNotOverrideEarlierInclude()
    {
        var result = _evaluator.Evaluate("SPORT DOCUMENTAIRE",
        [
            new CategoryRuleDefinition(1, 10, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true),
            new CategoryRuleDefinition(2, 20, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true)
        ]);

        Assert.Equal(CategoryInclusionDecision.Include, result.Decision);
        Assert.Equal(10, result.MatchedRuleSequence);
    }

    [Fact]
    public void LaterInclude_DoesNotOverrideEarlierExclude()
    {
        var result = _evaluator.Evaluate("SPORT DOCUMENTAIRE",
        [
            new CategoryRuleDefinition(1, 10, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true),
            new CategoryRuleDefinition(2, 20, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true)
        ]);

        Assert.Equal(CategoryInclusionDecision.Exclude, result.Decision);
        Assert.Equal(10, result.MatchedRuleSequence);
    }

    [Fact]
    public void DisabledRules_AreSkipped()
    {
        var result = _evaluator.Evaluate("SPORT",
        [
            new CategoryRuleDefinition(1, 10, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, false),
            new CategoryRuleDefinition(2, 20, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "SPORT", false, true)
        ]);

        Assert.Equal(CategoryInclusionDecision.Include, result.Decision);
        Assert.Equal(20, result.MatchedRuleSequence);
    }

    [Fact]
    public void NoMatchingRule_ReturnsInclude()
    {
        var result = _evaluator.Evaluate("|FR| COMEDIE",
        [
            new CategoryRuleDefinition(1, 10, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true)
        ]);

        Assert.Equal(CategoryInclusionDecision.Include, result.Decision);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Rules_AreEvaluatedBySequence()
    {
        var result = _evaluator.Evaluate("SPORT DOCUMENTAIRE",
        [
            new CategoryRuleDefinition(1, 20, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true),
            new CategoryRuleDefinition(2, 10, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true)
        ]);

        Assert.Equal(CategoryInclusionDecision.Include, result.Decision);
        Assert.Equal(10, result.MatchedRuleSequence);
    }

    [Fact]
    public void Rules_WithSameSequence_AreEvaluatedById()
    {
        var result = _evaluator.Evaluate("SPORT DOCUMENTAIRE",
        [
            new CategoryRuleDefinition(2, 10, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true),
            new CategoryRuleDefinition(1, 10, CategoryRuleAction.Include, CategoryRuleOperator.Contains, "DOCUMENTAIRE", false, true)
        ]);

        Assert.Equal(CategoryInclusionDecision.Include, result.Decision);
        Assert.Equal(1, result.MatchedRuleId);
    }

    private CategoryRuleEvaluationResult Evaluate(string categoryName, CategoryRuleOperator @operator, string pattern, bool caseSensitive, CategoryRuleAction action) =>
        _evaluator.Evaluate(categoryName,
        [
            new CategoryRuleDefinition(1, 10, action, @operator, pattern, caseSensitive, true)
        ]);
}
