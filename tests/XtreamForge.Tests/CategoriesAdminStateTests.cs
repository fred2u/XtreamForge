using XtreamForge.Blazor.Features.Categories;

namespace XtreamForge.Tests;

public sealed class CategoriesAdminStateTests
{
    [Fact]
    public void FilterCategoryRows_AppliesStatusAndSearchWithoutMutatingMasterList()
    {
        var rows = new List<CategoryRowState>
        {
            new(new AdminUpstreamCategory(
                1,
                "10",
                "|FR| SPORT",
                false,
                null,
                null,
                100,
                "|FR| SPORT",
                "Exclude",
                "Exclude",
                10,
                10,
                "Exclude",
                "Contains",
                "SPORT",
                false,
                false,
                "Original")),
            new(new AdminUpstreamCategory(
                2,
                "20",
                "|FR| MOVIES",
                false,
                null,
                null,
                101,
                "|FR| MOVIES",
                "Include",
                "Include",
                null,
                null,
                null,
                null,
                null,
                null,
                true,
                "Original"))
        };

        var filteredRows = CategoriesPageState.FilterCategoryRows(rows, "sport", CategoryStatusFilter.Disabled);

        var filteredRow = Assert.Single(filteredRows);
        Assert.Same(rows[0], filteredRow);
        Assert.Equal("|FR| SPORT", filteredRow.Summary.UpstreamCategoryName);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void RuleRowState_PopulatesEditorValuesFromPersistedSummary()
    {
        var summary = new AdminCategoryRule(7, 20, "Exclude", "Contains", "SPORT", false, true);

        var row = new RuleRowState(summary);

        Assert.Equal(summary.Id, row.RuleId);
        Assert.Equal(summary.Sequence, row.Sequence);
        Assert.Equal(summary.Action, row.Action.ToString());
        Assert.Equal(summary.Operator, row.Operator.ToString());
        Assert.Equal(summary.Pattern, row.Pattern);
        Assert.Equal(summary.CaseSensitive, row.CaseSensitive);
        Assert.Equal(summary.IsEnabled, row.IsEnabled);
    }

    [Fact]
    public void CustomCategoryRowState_PopulatesDisplayNameFromPersistedSummary()
    {
        var summary = new AdminCustomCategory(3, 4000, "Movies 4K", 2);

        var row = new CustomCategoryRowState(summary);

        Assert.Equal("Movies 4K", row.DisplayName);
    }

    [Fact]
    public void RuleRowState_DisablesSecondaryActionsWhileEditing()
    {
        var row = new RuleRowState(new AdminCategoryRule(7, 20, "Exclude", "Contains", "SPORT", false, true));

        Assert.True(row.CanRunSecondaryActions);

        row.BeginEdit();

        Assert.False(row.CanRunSecondaryActions);
    }

    [Fact]
    public void CategoryRowState_UsesExistingCustomCategorySelection()
    {
        var summary = new AdminUpstreamCategory(
            4,
            "50",
            "|FR| 4K UHD",
            false,
            9,
            "Movies 4K",
            500,
            "|FR| 4K UHD",
            "Include",
            "Include",
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            "Custom");

        var row = new CategoryRowState(summary);

        Assert.Equal("custom:9", row.MappingValue);
    }

    [Fact]
    public void CategoryRowState_CancelNewCustomCategoryRestoresPreviousSelection()
    {
        var summary = new AdminUpstreamCategory(
            4,
            "50",
            "|FR| 4K UHD",
            false,
            9,
            "Movies 4K",
            500,
            "|FR| 4K UHD",
            "Include",
            "Include",
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            "Custom");

        var row = new CategoryRowState(summary);
        row.MappingValue = "new";

        row.BeginCustomCategoryCreate();
        row.NewCustomCategoryName = "Fresh 4K";
        row.CancelCustomCategoryCreate();

        Assert.False(row.IsCreatingCustomCategory);
        Assert.Equal("custom:9", row.MappingValue);
        Assert.Null(row.NewCustomCategoryName);
    }

    [Fact]
    public void CategoryRowState_ApplyPersistedMapping_SelectsCreatedCustomCategoryImmediately()
    {
        var summary = new AdminUpstreamCategory(
            4,
            "50",
            "|FR| 4K UHD",
            false,
            null,
            null,
            500,
            "|FR| 4K UHD",
            "Include",
            "Include",
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            "Original");

        var row = new CategoryRowState(summary)
        {
            MappingValue = "new",
            NewCustomCategoryName = "Movies 4K"
        };

        row.BeginCustomCategoryCreate();
        row.BeginSave("new", row.NewCustomCategoryName);
        row.ApplyPersistedMapping("custom:42", new AdminCustomCategory(42, 1042, "Movies 4K", 1));

        Assert.False(row.IsCreatingCustomCategory);
        Assert.Equal("custom:42", row.MappingValue);
        Assert.Equal(42, row.Summary.CustomCategoryId);
        Assert.Equal("Movies 4K", row.Summary.CustomCategoryName);
        Assert.Equal("Custom", row.Summary.CurrentMappingSelection);
        Assert.Null(row.NewCustomCategoryName);
    }

    [Fact]
    public void FormatEffectiveStatus_PrefersManualDisabledThenRuleDisabled()
    {
        var manualSummary = new AdminUpstreamCategory(1, "10", "Manual", true, null, null, 100, "Manual", "Exclude", "Include", null, null, null, null, null, null, false, "Disabled");
        var ruleSummary = new AdminUpstreamCategory(2, "20", "Rule", false, null, null, 101, "Rule", "Exclude", "Exclude", 4, 20, "Exclude", "Contains", "SPORT", false, false, "Original");
        var enabledSummary = new AdminUpstreamCategory(3, "30", "Enabled", false, null, null, 102, "Enabled", "Include", "Include", null, null, null, null, null, null, true, "Original");

        Assert.Equal("Disabled · manual", CategoriesPageState.FormatEffectiveStatus(manualSummary));
        Assert.Equal("Disabled · rule", CategoriesPageState.FormatEffectiveStatus(ruleSummary));
        Assert.Equal("Enabled", CategoriesPageState.FormatEffectiveStatus(enabledSummary));
    }
}
