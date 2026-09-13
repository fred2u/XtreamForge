using XtreamForge.Api.Components;
using XtreamForge.Infrastructure.Models;
using XtreamForge.Infrastructure.Services;

namespace XtreamForge.Api.Tests;

public sealed class CategoriesAdminStateTests
{
    [Fact]
    public void FilterCategoryRows_AppliesStatusAndSearchWithoutMutatingMasterList()
    {
        var rows = new List<CategoryRowState>
        {
            new(new UpstreamCategorySummary(
                1,
                "10",
                "|FR| SPORT",
                false,
                null,
                null,
                100,
                "|FR| SPORT",
                CategoryInclusionDecision.Exclude,
                CategoryInclusionDecision.Exclude,
                10,
                10,
                CategoryRuleAction.Exclude,
                CategoryRuleOperator.Contains,
                "SPORT",
                false,
                false,
                CategoryMappingSelection.Original)),
            new(new UpstreamCategorySummary(
                2,
                "20",
                "|FR| MOVIES",
                false,
                null,
                null,
                101,
                "|FR| MOVIES",
                CategoryInclusionDecision.Include,
                CategoryInclusionDecision.Include,
                null,
                null,
                null,
                null,
                null,
                null,
                true,
                CategoryMappingSelection.Original))
        };

        var filteredRows = CategoriesAdminState.FilterCategoryRows(rows, "sport", CategoryStatusFilter.Disabled);

        var filteredRow = Assert.Single(filteredRows);
        Assert.Equal("|FR| SPORT", filteredRow.Summary.UpstreamCategoryName);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void RuleRowState_PopulatesEditorValuesFromPersistedSummary()
    {
        var summary = new CategoryRuleSummary(7, 20, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true);

        var row = new RuleRowState(summary);

        Assert.Equal(summary.Id, row.RuleId);
        Assert.Equal(summary.Sequence, row.Sequence);
        Assert.Equal(summary.Action, row.Action);
        Assert.Equal(summary.Operator, row.Operator);
        Assert.Equal(summary.Pattern, row.Pattern);
        Assert.Equal(summary.CaseSensitive, row.CaseSensitive);
        Assert.Equal(summary.IsEnabled, row.IsEnabled);
    }

    [Fact]
    public void CustomCategoryRowState_PopulatesDisplayNameFromPersistedSummary()
    {
        var summary = new CustomCategorySummary(3, 4000, "Movies 4K", 2);

        var row = new CustomCategoryRowState(summary);

        Assert.Equal("Movies 4K", row.DisplayName);
    }

    [Fact]
    public void CategoryRowState_UsesExistingCustomCategorySelection()
    {
        var summary = new UpstreamCategorySummary(
            4,
            "50",
            "|FR| 4K UHD",
            false,
            9,
            "Movies 4K",
            500,
            "|FR| 4K UHD",
            CategoryInclusionDecision.Include,
            CategoryInclusionDecision.Include,
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            CategoryMappingSelection.Custom);

        var row = new CategoryRowState(summary);

        Assert.Equal("custom:9", row.MappingValue);
    }

    [Fact]
    public void CategoryRowState_CancelNewCustomCategoryRestoresPreviousSelection()
    {
        var summary = new UpstreamCategorySummary(
            4,
            "50",
            "|FR| 4K UHD",
            false,
            9,
            "Movies 4K",
            500,
            "|FR| 4K UHD",
            CategoryInclusionDecision.Include,
            CategoryInclusionDecision.Include,
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            CategoryMappingSelection.Custom);

        var row = new CategoryRowState(summary);
        row.MappingValue = "new";

        row.BeginCustomCategoryCreate();
        row.NewCustomCategoryName = "Fresh 4K";
        row.CancelCustomCategoryCreate();

        Assert.False(row.IsCreatingCustomCategory);
        Assert.Equal("custom:9", row.MappingValue);
        Assert.Null(row.NewCustomCategoryName);
    }
}
