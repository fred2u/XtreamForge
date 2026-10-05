using XtreamForge.ApiService.Services;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.Tests.Services;

public class RuleEvaluatorTmdbTests
{
    [Fact]
    public void EvaluateTmdbInfos_WhenNoRuleMatches_IncludesWithoutDecidingRule()
    {
        var evaluation = Evaluate(CreateInfo(), Rule(10, TmdbRuleField.Title, RuleAction.Exclude, RuleOperator.Contains, "Mossbeard"));

        Assert.Equal((InclusionDecision.Include, (TmdbExclusionReason?)null, (TmdbRule?)null), (evaluation.Decision, evaluation.ExclusionReason, evaluation.DecidingRule));
    }

    [Fact]
    public void EvaluateTmdbInfos_WhenExcludedManually_ExcludesBeforeAnyRule()
    {
        var info = CreateInfo();
        info.IsExcluded = true;

        var evaluation = Evaluate(info, Rule(10, TmdbRuleField.Title, RuleAction.Include, RuleOperator.Contains, "Lattice"));

        Assert.Equal((InclusionDecision.Exclude, TmdbExclusionReason.ManuallyExcluded), (evaluation.Decision, evaluation.ExclusionReason));
        Assert.Null(evaluation.DecidingRule);
    }

    [Fact]
    public void EvaluateTmdbInfos_FirstMatchingEnabledRuleBySequenceDecides()
    {
        var disabled = Rule(1, TmdbRuleField.Title, RuleAction.Exclude, RuleOperator.Contains, "Lattice");
        disabled.IsEnabled = false;
        var include = Rule(20, TmdbRuleField.Title, RuleAction.Include, RuleOperator.StartsWith, "the");
        var exclude = Rule(10, TmdbRuleField.Genre, RuleAction.Exclude, RuleOperator.Contains, "Science");

        var evaluation = Evaluate(CreateInfo(), include, disabled, exclude);

        Assert.Equal((InclusionDecision.Exclude, TmdbExclusionReason.Rule, exclude), (evaluation.Decision, evaluation.ExclusionReason, evaluation.DecidingRule));
    }

    [Fact]
    public void EvaluateTmdbInfos_WhenIncludeRuleMatches_IncludesWithDecidingRule()
    {
        var include = Rule(10, TmdbRuleField.Title, RuleAction.Include, RuleOperator.Contains, "lattice");

        var evaluation = Evaluate(CreateInfo(), include, Rule(20, TmdbRuleField.Title, RuleAction.Exclude, RuleOperator.Contains, "Lattice"));

        Assert.Equal((InclusionDecision.Include, (TmdbExclusionReason?)null, include), (evaluation.Decision, evaluation.ExclusionReason, evaluation.DecidingRule));
    }

    [Theory]
    [InlineData(RuleOperator.Contains, "horror", false, true)]
    [InlineData(RuleOperator.Contains, "Horror", true, true)]
    [InlineData(RuleOperator.Contains, "horror", true, false)]
    [InlineData(RuleOperator.Contains, "Comedy", false, false)]
    [InlineData(RuleOperator.StartsWith, "Sci", false, true)]
    [InlineData(RuleOperator.StartsWith, "Fiction", false, false)]
    [InlineData(RuleOperator.NotContains, "Comedy", false, true)]
    [InlineData(RuleOperator.NotContains, "Horror", false, false)]
    [InlineData(RuleOperator.NotStartsWith, "Dra", false, true)]
    [InlineData(RuleOperator.NotStartsWith, "Hor", false, false)]
    public void IsMatch_ForGenre_EvaluatesEachGenre(RuleOperator @operator, string pattern, bool caseSensitive, bool expected)
    {
        var rule = Rule(10, TmdbRuleField.Genre, RuleAction.Exclude, @operator, pattern);
        rule.CaseSensitive = caseSensitive;

        Assert.Equal(expected, RuleEvaluator.IsMatch(CreateInfo(), rule));
    }

    [Theory]
    [InlineData(RuleOperator.Contains, false)]
    [InlineData(RuleOperator.StartsWith, false)]
    [InlineData(RuleOperator.NotContains, true)]
    [InlineData(RuleOperator.NotStartsWith, true)]
    public void IsMatch_ForGenre_WithoutGenres_MatchesOnlyNegativeOperators(RuleOperator @operator, bool expected)
    {
        var info = CreateInfo();
        info.Genres = [];

        // "does not …" matches when no genre matches, which is always the case without genres
        Assert.Equal(expected, RuleEvaluator.IsMatch(info, Rule(10, TmdbRuleField.Genre, RuleAction.Exclude, @operator, "Horror")));
    }

    [Theory]
    [InlineData(RuleOperator.StartsWith, "The", true)]
    [InlineData(RuleOperator.Contains, "atti", true)]
    [InlineData(RuleOperator.NotContains, "Lattice", false)]
    [InlineData(RuleOperator.NotStartsWith, "Lattice", true)]
    public void IsMatch_ForTitle_MatchesTheTmdbTitle(RuleOperator @operator, string pattern, bool expected)
    {
        Assert.Equal(expected, RuleEvaluator.IsMatch(CreateInfo(), Rule(10, TmdbRuleField.Title, RuleAction.Exclude, @operator, pattern)));
    }

    [Fact]
    public void IsMatch_ForTitle_WithoutTitle_UsesAnEmptyTitle()
    {
        var info = CreateInfo();
        info.Title = null;

        Assert.False(RuleEvaluator.IsMatch(info, Rule(10, TmdbRuleField.Title, RuleAction.Exclude, RuleOperator.Contains, "Lattice")));
        Assert.True(RuleEvaluator.IsMatch(info, Rule(10, TmdbRuleField.Title, RuleAction.Exclude, RuleOperator.NotContains, "Lattice")));
    }

    private static TmdbRuleEvaluation Evaluate(TmdbInfo info, params TmdbRule[] rules) => Assert.Single(RuleEvaluator.EvaluateTmdbInfos([info], rules));

    private static TmdbInfo CreateInfo() => new()
    {
        TmdbId = 603,
        ContentType = ContentType.Vod,
        Title = "The Lattice",
        Genres = ["Action", "Science Fiction", "Horror"]
    };

    private static TmdbRule Rule(int sequence, TmdbRuleField field, RuleAction action, RuleOperator @operator, string pattern) => new()
    {
        Id = sequence,
        ContentType = ContentType.Vod,
        Sequence = sequence,
        Field = field,
        Action = action,
        Operator = @operator,
        Pattern = pattern
    };
}
