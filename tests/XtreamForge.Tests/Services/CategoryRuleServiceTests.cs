using XtreamForge.ApiService.Services;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;

namespace XtreamForge.Tests.Services;

public class CategoryRuleServiceTests
{
    [Fact]
    public void ApplyRules_ExcludesCategoriesAlreadyMarkedExcluded()
    {
        var categories = new[]
        {
            new XtreamCategory
            {
                Name = "Private",
                XtreamId = "c1",
                IsExcluded = true
            },
            new XtreamCategory
            {
                Name = "Public",
                XtreamId = "c2"
            }
        };

        var includedCategories = CategoryRuleService.ApplyRules(categories, [])
            .ToList();

        var item = Assert.Single(includedCategories);
        Assert.Equal("Public", item.Name);
    }

    [Fact]
    public void ApplyRules_UsesFirstMatchingRule()
    {
        var category = new XtreamCategory
        {
            Name = "Sports Arena",
            XtreamId = "c1"
        };

        var rules = new List<CategoryRule>
        {
            new()
            {
                Pattern = "Sports",
                Operator = RuleOperator.StartsWith,
                Action = RuleAction.Exclude,
                CaseSensitive = false,
                Sequence = 1
            },
            new()
            {
                Pattern = "Arena",
                Operator = RuleOperator.Contains,
                Action = RuleAction.Include,
                Sequence = 2
            }
        };

        var result = CategoryRuleService.ApplyRules([category], rules).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void ApplyRules_IncludesWhenNoRuleMatches()
    {
        var category = new XtreamCategory
        {
            Name = "Documentary",
            XtreamId = "c1"
        };

        var rules = new List<CategoryRule>
        {
            new()
            {
                Pattern = "Sports",
                Operator = RuleOperator.Contains,
                Action = RuleAction.Exclude,
                Sequence = 1
            }
        };

        var result = CategoryRuleService.ApplyRules([category], rules).ToList();

        var item = Assert.Single(result);
        Assert.Equal("Documentary", item.Name);
    }

    [Fact]
    public void ApplyRules_SkipsDisabledRules()
    {
        var category = new XtreamCategory
        {
            Name = "Kids",
            XtreamId = "c1"
        };

        var rules = new List<CategoryRule>
        {
            new()
            {
                Pattern = "Kids",
                Operator = RuleOperator.StartsWith,
                Action = RuleAction.Exclude,
                CaseSensitive = false,
                Sequence = 1,
                IsEnabled = false
            },
            new()
            {
                Pattern = "Kids",
                Operator = RuleOperator.StartsWith,
                Action = RuleAction.Include,
                Sequence = 2,
                CaseSensitive = false
            }
        };

        var result = CategoryRuleService.ApplyRules([category], rules).ToList();

        var item = Assert.Single(result);
        Assert.Equal("Kids", item.Name);
    }

    [Fact]
    public void ApplyRules_CaseInsensitiveContainsMatch()
    {
        var category = new XtreamCategory
        {
            Name = "Morning News",
            XtreamId = "c1"
        };

        var rules = new List<CategoryRule>
        {
            new()
            {
                Pattern = "news",
                Operator = RuleOperator.Contains,
                Action = RuleAction.Exclude,
                Sequence = 1,
                CaseSensitive = false
            }
        };

        var result = CategoryRuleService.ApplyRules([category], rules).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void Evaluate_WhenManuallyExcluded_ReturnsManualExclusionEvenIfRuleMatches()
    {
        var category = new XtreamCategory { Name = "Kids", XtreamId = "c1", IsExcluded = true, IsEnabled = false };
        var rules = new List<CategoryRule> { ExcludeRule("Kids", 1) };

        var evaluation = Assert.Single(CategoryRuleService.Evaluate([category], rules));

        Assert.Equal(InclusionDecision.Exclude, evaluation.Decision);
        Assert.Equal(CategoryExclusionReason.ManuallyExcluded, evaluation.ExclusionReason);
        Assert.Null(evaluation.DecidingRule);
    }

    [Fact]
    public void Evaluate_WhenDisabledByProvider_ReturnsProviderDisabledEvenIfRuleMatches()
    {
        var category = new XtreamCategory { Name = "Kids", XtreamId = "c1", IsEnabled = false };
        var rules = new List<CategoryRule> { ExcludeRule("Kids", 1) };

        var evaluation = Assert.Single(CategoryRuleService.Evaluate([category], rules));

        Assert.Equal(InclusionDecision.Exclude, evaluation.Decision);
        Assert.Equal(CategoryExclusionReason.ProviderDisabled, evaluation.ExclusionReason);
        Assert.Null(evaluation.DecidingRule);
    }

    [Fact]
    public void Evaluate_ReturnsFirstMatchingEnabledRuleBySequence()
    {
        var category = new XtreamCategory { Name = "Kids Movies", XtreamId = "c1" };
        var disabledRule = ExcludeRule("Kids", 1);
        disabledRule.IsEnabled = false;
        var laterRule = ExcludeRule("Movies", 30);
        var earlierRule = ExcludeRule("Kids", 20);
        var rules = new List<CategoryRule> { disabledRule, laterRule, earlierRule };

        var evaluation = Assert.Single(CategoryRuleService.Evaluate([category], rules));

        Assert.Equal(InclusionDecision.Exclude, evaluation.Decision);
        Assert.Equal(CategoryExclusionReason.Rule, evaluation.ExclusionReason);
        Assert.Same(earlierRule, evaluation.DecidingRule);
    }

    [Fact]
    public void Evaluate_WhenCaseSensitiveRuleDoesNotMatch_IncludesWithoutRule()
    {
        var category = new XtreamCategory { Name = "kids", XtreamId = "c1" };
        var rule = ExcludeRule("Kids", 1);
        rule.CaseSensitive = true;

        var evaluation = Assert.Single(CategoryRuleService.Evaluate([category], [rule]));

        Assert.Equal(InclusionDecision.Include, evaluation.Decision);
        Assert.Null(evaluation.ExclusionReason);
        Assert.Null(evaluation.DecidingRule);
    }

    [Theory]
    [InlineData("Kids Movies", "sport", false, InclusionDecision.Exclude)]
    [InlineData("Sports Live", "sport", false, InclusionDecision.Include)]
    [InlineData("Sports Live", "sport", true, InclusionDecision.Exclude)]
    public void Evaluate_NotContainsRule_MatchesNamesWithoutPattern(string name, string pattern, bool caseSensitive, InclusionDecision expected)
    {
        var category = new XtreamCategory { Name = name, XtreamId = "c1" };
        var rule = ExcludeRule(pattern, 1);
        rule.Operator = RuleOperator.NotContains;
        rule.CaseSensitive = caseSensitive;

        var evaluation = Assert.Single(CategoryRuleService.Evaluate([category], [rule]));

        Assert.Equal(expected, evaluation.Decision);
        Assert.Equal(expected == InclusionDecision.Exclude ? rule : null, evaluation.DecidingRule);
    }

    [Theory]
    [InlineData("Kids Movies", "sport", false, InclusionDecision.Exclude)]
    [InlineData("Sports Live", "sport", false, InclusionDecision.Include)]
    [InlineData("Sports Live", "sport", true, InclusionDecision.Exclude)]
    [InlineData("Live Sports", "sport", false, InclusionDecision.Exclude)]
    public void Evaluate_NotStartsWithRule_MatchesNamesNotStartingWithPattern(string name, string pattern, bool caseSensitive, InclusionDecision expected)
    {
        var category = new XtreamCategory { Name = name, XtreamId = "c1" };
        var rule = ExcludeRule(pattern, 1);
        rule.Operator = RuleOperator.NotStartsWith;
        rule.CaseSensitive = caseSensitive;

        var evaluation = Assert.Single(CategoryRuleService.Evaluate([category], [rule]));

        Assert.Equal(expected, evaluation.Decision);
        Assert.Equal(expected == InclusionDecision.Exclude ? rule : null, evaluation.DecidingRule);
    }

    private static CategoryRule ExcludeRule(string pattern, int sequence) => new()
    {
        Pattern = pattern,
        Operator = RuleOperator.Contains,
        Action = RuleAction.Exclude,
        Sequence = sequence
    };
}
