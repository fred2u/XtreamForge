using XtreamForge.Infrastructure.Models;
using XtreamForge.Infrastructure.Services;

namespace XtreamForge.Api.Components;

internal static class CategoriesAdminState
{
    public static IReadOnlyList<CategoryRowState> FilterCategoryRows(
        IReadOnlyList<CategoryRowState> rows,
        string? searchText,
        CategoryStatusFilter statusFilter) =>
        rows
            .Where(row => MatchesSearch(row, searchText))
            .Where(row => MatchesStatus(row, statusFilter))
            .ToList();

    private static bool MatchesSearch(CategoryRowState row, string? searchText) =>
        string.IsNullOrWhiteSpace(searchText)
        || row.Summary.UpstreamCategoryName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        || row.Summary.UpstreamCategoryId.Contains(searchText, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesStatus(CategoryRowState row, CategoryStatusFilter statusFilter) => statusFilter switch
    {
        CategoryStatusFilter.Enabled => row.Summary.EffectiveDecision == CategoryInclusionDecision.Include,
        CategoryStatusFilter.Disabled => row.Summary.EffectiveDecision == CategoryInclusionDecision.Exclude,
        _ => true
    };
}

internal sealed class CategoryRowState(UpstreamCategorySummary summary)
{
    public UpstreamCategorySummary Summary { get; } = summary;

    public string MappingValue { get; set; } = GetMappingValue(summary);

    public string SavedMappingValue { get; private set; } = GetMappingValue(summary);

    public bool IsCreatingCustomCategory { get; private set; }

    public string? NewCustomCategoryName { get; set; }

    public string? Message { get; set; }

    public void BeginCustomCategoryCreate()
    {
        IsCreatingCustomCategory = true;
        NewCustomCategoryName ??= Summary.UpstreamCategoryName;
    }

    public void CancelCustomCategoryCreate()
    {
        IsCreatingCustomCategory = false;
        MappingValue = SavedMappingValue;
        NewCustomCategoryName = null;
        Message = null;
    }

    public void CommitMapping(string mappingValue)
    {
        SavedMappingValue = mappingValue;
        MappingValue = mappingValue;
        IsCreatingCustomCategory = false;
        NewCustomCategoryName = null;
    }

    private static string GetMappingValue(UpstreamCategorySummary summary) => summary.CurrentMappingSelection switch
    {
        CategoryMappingSelection.Disabled => "disabled",
        CategoryMappingSelection.Original => "original",
        CategoryMappingSelection.Custom when summary.CustomCategoryId is int customCategoryId => GetCustomMappingValue(customCategoryId),
        _ => "original"
    };

    public static string GetCustomMappingValue(int customCategoryId) => $"custom:{customCategoryId}";
}

internal sealed class RuleRowState(CategoryRuleSummary summary)
{
    public int RuleId { get; } = summary.Id;

    public int Sequence { get; set; } = summary.Sequence;

    public CategoryRuleAction Action { get; set; } = summary.Action;

    public CategoryRuleOperator Operator { get; set; } = summary.Operator;

    public string Pattern { get; set; } = summary.Pattern;

    public bool CaseSensitive { get; set; } = summary.CaseSensitive;

    public bool IsEnabled { get; set; } = summary.IsEnabled;

    public bool ConfirmDelete { get; set; }

    public string? Message { get; set; }
}

internal sealed class RuleEditorState
{
    public CategoryRuleAction Action { get; set; } = CategoryRuleAction.Include;

    public CategoryRuleOperator Operator { get; set; } = CategoryRuleOperator.Contains;

    public string? Pattern { get; set; }

    public bool CaseSensitive { get; set; }

    public bool IsEnabled { get; set; } = true;
}

internal sealed class CustomCategoryRowState(CustomCategorySummary summary)
{
    public CustomCategorySummary Summary { get; } = summary;

    public string DisplayName { get; set; } = summary.DisplayName;
}

internal sealed class CustomCategoryEditorState
{
    public string? DisplayName { get; set; }
}

internal enum CategoryStatusFilter
{
    All = 1,
    Enabled = 2,
    Disabled = 3
}
