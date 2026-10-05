using System.Text.Json.Nodes;
using XtreamForge.ApiService.Services;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;

namespace XtreamForge.Tests.Services;

public class RuleEvaluatorItemTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "name": "" }""")]
    [InlineData("""{ "name": "   " }""")]
    public void EvaluateItem_WhenNameIsMissingOrBlank_ReturnsExclude(string json)
    {
        var item = Assert.IsType<JsonObject>(JsonNode.Parse(json));

        var decision = RuleEvaluator.EvaluateItem(item, []);

        Assert.Equal(InclusionDecision.Exclude, decision);
    }

    [Fact]
    public void EvaluateItem_WhenNoRuleMatches_ReturnsInclude()
    {
        var item = CreateItem("Documentary");
        var rules = new List<ItemRule>
        {
            CreateRule(1, RuleAction.Exclude, RuleOperator.Contains, "Sports")
        };

        var decision = RuleEvaluator.EvaluateItem(item, rules);

        Assert.Equal(InclusionDecision.Include, decision);
    }

    [Fact]
    public void EvaluateItem_UsesFirstMatchingRule()
    {
        var item = CreateItem("Sports Arena");
        var rules = new List<ItemRule>
        {
            CreateRule(1, RuleAction.Exclude, RuleOperator.StartsWith, "Sports"),
            CreateRule(2, RuleAction.Include, RuleOperator.Contains, "Arena")
        };

        var decision = RuleEvaluator.EvaluateItem(item, rules);

        Assert.Equal(InclusionDecision.Exclude, decision);
    }

    [Fact]
    public void EvaluateItem_IncludeRuleMatchingFirst_ReturnsInclude()
    {
        var item = CreateItem("Sports Arena");
        var rules = new List<ItemRule>
        {
            CreateRule(1, RuleAction.Include, RuleOperator.Contains, "Arena"),
            CreateRule(2, RuleAction.Exclude, RuleOperator.StartsWith, "Sports")
        };

        var decision = RuleEvaluator.EvaluateItem(item, rules);

        Assert.Equal(InclusionDecision.Include, decision);
    }

    [Theory]
    [InlineData(RuleOperator.StartsWith, "morning", false, InclusionDecision.Exclude)]
    [InlineData(RuleOperator.StartsWith, "morning", true, InclusionDecision.Include)]
    [InlineData(RuleOperator.StartsWith, "News", false, InclusionDecision.Include)]
    [InlineData(RuleOperator.Contains, "news", false, InclusionDecision.Exclude)]
    [InlineData(RuleOperator.Contains, "news", true, InclusionDecision.Include)]
    [InlineData(RuleOperator.Contains, "News", true, InclusionDecision.Exclude)]
    [InlineData(RuleOperator.NotContains, "sports", false, InclusionDecision.Exclude)]
    [InlineData(RuleOperator.NotContains, "news", false, InclusionDecision.Include)]
    [InlineData(RuleOperator.NotContains, "news", true, InclusionDecision.Exclude)]
    [InlineData(RuleOperator.NotStartsWith, "news", false, InclusionDecision.Exclude)]
    [InlineData(RuleOperator.NotStartsWith, "morning", false, InclusionDecision.Include)]
    [InlineData(RuleOperator.NotStartsWith, "morning", true, InclusionDecision.Exclude)]
    public void EvaluateItem_RespectsOperatorAndCaseSensitivity(
        RuleOperator @operator,
        string pattern,
        bool caseSensitive,
        InclusionDecision expected)
    {
        var item = CreateItem("Morning News");
        var rules = new List<ItemRule>
        {
            CreateRule(1, RuleAction.Exclude, @operator, pattern, caseSensitive)
        };

        var decision = RuleEvaluator.EvaluateItem(item, rules);

        Assert.Equal(expected, decision);
    }

    private static JsonObject CreateItem(string name) => new() { ["name"] = name };

    private static ItemRule CreateRule(
        int sequence,
        RuleAction action,
        RuleOperator @operator,
        string pattern,
        bool caseSensitive = false) => new()
        {
            Sequence = sequence,
            Action = action,
            Operator = @operator,
            Pattern = pattern,
            CaseSensitive = caseSensitive
        };
}
