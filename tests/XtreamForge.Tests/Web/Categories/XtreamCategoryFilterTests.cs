using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Tests.Web.Categories;

public class XtreamCategoryFilterTests
{
    [Fact]
    public void DefaultFilter_IsEmptyAndMatchesEverything()
    {
        var filter = new XtreamCategoryFilter();

        Assert.True(filter.IsEmpty);
        Assert.True(filter.Matches(Category(isExcluded: true, isEnabled: false, decision: InclusionDecision.Exclude)));
    }

    [Theory]
    [InlineData("action", true)]
    [InlineData("X-12", true)]
    [InlineData("thrillers", true)]
    [InlineData("  ACTION  ", true)]
    [InlineData("comedy", false)]
    public void Matches_SearchesNameXtreamIdAndCustomCategoryIgnoringCase(string search, bool expected)
    {
        var filter = new XtreamCategoryFilter(Search: search);

        Assert.Equal(expected, filter.Matches(Category(name: "Action HD", xtreamId: "x-12", customCategoryId: 5, customCategoryName: "Thrillers")));
    }

    [Theory]
    [InlineData(MappingFilter.Mapped, 5, true)]
    [InlineData(MappingFilter.Mapped, null, false)]
    [InlineData(MappingFilter.Unmapped, null, true)]
    [InlineData(MappingFilter.Unmapped, 5, false)]
    public void Matches_FiltersByMapping(MappingFilter mapping, int? customCategoryId, bool expected)
    {
        var filter = new XtreamCategoryFilter(Mapping: mapping);

        Assert.Equal(expected, filter.Matches(Category(customCategoryId: customCategoryId)));
    }

    [Theory]
    [InlineData(ManualExclusionFilter.NotExcluded, false, true)]
    [InlineData(ManualExclusionFilter.NotExcluded, true, false)]
    [InlineData(ManualExclusionFilter.Excluded, true, true)]
    [InlineData(ManualExclusionFilter.Excluded, false, false)]
    public void Matches_FiltersByManualExclusion(ManualExclusionFilter manualExclusion, bool isExcluded, bool expected)
    {
        var filter = new XtreamCategoryFilter(ManualExclusion: manualExclusion);

        Assert.Equal(expected, filter.Matches(Category(isExcluded: isExcluded)));
    }

    [Theory]
    [InlineData(DecisionFilter.Included, InclusionDecision.Include, true)]
    [InlineData(DecisionFilter.Included, InclusionDecision.Exclude, false)]
    [InlineData(DecisionFilter.Excluded, InclusionDecision.Exclude, true)]
    [InlineData(DecisionFilter.Excluded, InclusionDecision.Include, false)]
    public void Matches_FiltersByDecisionOfTheApi(DecisionFilter decisionFilter, InclusionDecision decision, bool expected)
    {
        var filter = new XtreamCategoryFilter(Decision: decisionFilter);

        // A category that is not excluded manually and enabled can still be excluded by a rule: only the API decision counts.
        Assert.Equal(expected, filter.Matches(Category(decision: decision)));
    }

    [Theory]
    [InlineData(ProviderStateFilter.Enabled, true, true)]
    [InlineData(ProviderStateFilter.Enabled, false, false)]
    [InlineData(ProviderStateFilter.Disabled, false, true)]
    [InlineData(ProviderStateFilter.Disabled, true, false)]
    public void Matches_FiltersByProviderState(ProviderStateFilter providerState, bool isEnabled, bool expected)
    {
        var filter = new XtreamCategoryFilter(ProviderState: providerState);

        Assert.Equal(expected, filter.Matches(Category(isEnabled: isEnabled)));
    }

    [Fact]
    public void Matches_CombinesFilters()
    {
        var filter = new XtreamCategoryFilter(Search: "kids", Decision: DecisionFilter.Excluded, Mapping: MappingFilter.Unmapped);

        Assert.True(filter.Matches(Category(name: "Kids", decision: InclusionDecision.Exclude)));
        Assert.False(filter.Matches(Category(name: "Kids", decision: InclusionDecision.Include)));
        Assert.False(filter.Matches(Category(name: "Kids", decision: InclusionDecision.Exclude, customCategoryId: 3)));
    }

    private static XtreamCategoryDto Category(
        string name = "Category",
        string xtreamId = "1",
        bool isEnabled = true,
        bool isExcluded = false,
        int? customCategoryId = null,
        string? customCategoryName = null,
        InclusionDecision decision = InclusionDecision.Include) =>
        new(10, xtreamId, name, ContentType.Vod, isEnabled, isExcluded, customCategoryId, customCategoryName, decision, null, null);
}
