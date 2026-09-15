namespace XtreamForge.Blazor.Features.Categories;

public static class CategoriesPageState
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
        CategoryStatusFilter.Enabled => string.Equals(row.Summary.EffectiveDecision, "Include", StringComparison.OrdinalIgnoreCase),
        CategoryStatusFilter.Disabled => string.Equals(row.Summary.EffectiveDecision, "Exclude", StringComparison.OrdinalIgnoreCase),
        _ => true
    };
}

public sealed class CategoryRowState(AdminUpstreamCategory Summary)
{
    public AdminUpstreamCategory Summary { get; } = Summary;

    public string MappingValue { get; set; } = GetMappingValue(Summary);

    public string SavedMappingValue { get; private set; } = GetMappingValue(Summary);

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
        Message = null;
    }

    private static string GetMappingValue(AdminUpstreamCategory summary) => summary.CurrentMappingSelection switch
    {
        "Disabled" => "disabled",
        "Original" => "original",
        "Custom" when summary.CustomCategoryId is int customCategoryId => GetCustomMappingValue(customCategoryId),
        _ => "original"
    };

    public static string GetCustomMappingValue(int customCategoryId) => $"custom:{customCategoryId}";
}

public sealed class RuleRowState(AdminCategoryRule Summary)
{
    public int RuleId { get; } = Summary.Id;

    public int Sequence { get; set; } = Summary.Sequence;

    public CategoryRuleActionOption Action { get; set; } = ParseAction(Summary.Action);

    public CategoryRuleOperatorOption Operator { get; set; } = ParseOperator(Summary.Operator);

    public string Pattern { get; set; } = Summary.Pattern;

    public bool CaseSensitive { get; set; } = Summary.CaseSensitive;

    public bool IsEnabled { get; set; } = Summary.IsEnabled;

    public bool ConfirmDelete { get; set; }

    public string? Message { get; set; }

    private static CategoryRuleActionOption ParseAction(string value) =>
        Enum.TryParse<CategoryRuleActionOption>(value, true, out var action)
            ? action
            : CategoryRuleActionOption.Include;

    private static CategoryRuleOperatorOption ParseOperator(string value) =>
        Enum.TryParse<CategoryRuleOperatorOption>(value, true, out var categoryRuleOperator)
            ? categoryRuleOperator
            : CategoryRuleOperatorOption.Contains;
}

public sealed class RuleEditorState
{
    public CategoryRuleActionOption Action { get; set; } = CategoryRuleActionOption.Include;

    public CategoryRuleOperatorOption Operator { get; set; } = CategoryRuleOperatorOption.Contains;

    public string? Pattern { get; set; }

    public bool CaseSensitive { get; set; }

    public bool IsEnabled { get; set; } = true;
}

public sealed class CustomCategoryRowState(AdminCustomCategory Summary)
{
    public AdminCustomCategory Summary { get; } = Summary;

    public string DisplayName { get; set; } = Summary.DisplayName;
}

public sealed class CustomCategoryEditorState
{
    public string? DisplayName { get; set; }
}

public enum CategoryStatusFilter
{
    All = 1,
    Enabled = 2,
    Disabled = 3
}

public enum AdminContentType
{
    Vod = 1,
    Series = 2
}

public enum CategoryRuleActionOption
{
    Include = 1,
    Exclude = 2
}

public enum CategoryRuleOperatorOption
{
    Contains = 1,
    StartsWith = 2
}

public sealed record AdminCategoriesPayload(
    IReadOnlyList<AdminSource> Sources,
    int? SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<AdminCustomCategory> CustomCategories,
    IReadOnlyList<AdminUpstreamCategory> UpstreamCategories);

public sealed record AdminSource(int Id, string Protocol, string Host, int Port, DateTimeOffset LastSeenAtUtc)
{
    public string DisplayName => $"{Protocol}://{Host}:{Port}";
}

public sealed record AdminCustomCategory(int Id, int XtreamForgeCategoryId, string DisplayName, int UsageCount);

public sealed record AdminUpstreamCategory(
    int Id,
    string UpstreamCategoryId,
    string UpstreamCategoryName,
    bool IsManuallyExcluded,
    int? CustomCategoryId,
    string? CustomCategoryName,
    int DedicatedXtreamForgeCategoryId,
    string DedicatedOutputName,
    string EffectiveDecision,
    string RuleDecision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    string? MatchedRuleAction,
    string? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive,
    bool IsEffectivelyIncluded,
    string CurrentMappingSelection);

public sealed record AdminCategoryRulesPayload(
    int? SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<AdminCategoryRule> Rules);

public sealed record AdminCategoryRule(
    int Id,
    int Sequence,
    string Action,
    string Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record AdminCategoryMappingUpdate(
    int SelectedSourceId,
    string SelectedContentType,
    string MappingSelection,
    int? CustomCategoryId,
    string? NewCustomCategoryName);

public sealed record AdminCategoryRuleUpdate(
    int SelectedSourceId,
    string SelectedContentType,
    string Action,
    string Operator,
    string? Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record AdminCustomCategoryCreate(string SelectedContentType, string? DisplayName);

public sealed record AdminCustomCategoryUpdate(string SelectedContentType, string DisplayName);

internal sealed record AdminErrorResponse(string Message);
