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

    public static string FormatEffectiveStatus(AdminUpstreamCategory summary)
    {
        if (summary.IsManuallyExcluded)
        {
            return "Disabled · manual";
        }

        if (!summary.IsEffectivelyIncluded)
        {
            return summary.MatchedRuleId is null
                ? "Disabled"
                : "Disabled · rule";
        }

        return "Enabled";
    }

    public static string? FormatMatchedRule(AdminUpstreamCategory summary) =>
        summary.MatchedRuleId is int && summary.MatchedRuleSequence is int matchedRuleSequence
            ? $"#{matchedRuleSequence} {summary.MatchedRuleAction} · {summary.MatchedPattern}"
            : null;

    public static string? FormatMatchedRuleDetails(AdminUpstreamCategory summary)
    {
        if (summary.MatchedRuleId is null)
        {
            return null;
        }

        var caseLabel = summary.MatchedRuleCaseSensitive == true ? "Case sensitive" : "Ignore case";
        return $"{summary.MatchedRuleOperator} · {caseLabel}";
    }

    private static bool MatchesSearch(CategoryRowState row, string? searchText) =>
        string.IsNullOrWhiteSpace(searchText)
        || row.Summary.UpstreamCategoryName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        || row.Summary.UpstreamCategoryId.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        || (row.Summary.CustomCategoryName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool MatchesStatus(CategoryRowState row, CategoryStatusFilter statusFilter) => statusFilter switch
    {
        CategoryStatusFilter.Enabled => row.Summary.IsEffectivelyIncluded,
        CategoryStatusFilter.Disabled => !row.Summary.IsEffectivelyIncluded,
        _ => true
    };
}

public sealed class CategoryRowState(AdminUpstreamCategory summary)
{
    public AdminUpstreamCategory Summary { get; private set; } = summary;

    public string MappingValue { get; set; } = GetMappingValue(summary);

    public string SavedMappingValue { get; private set; } = GetMappingValue(summary);

    public bool IsCreatingCustomCategory { get; private set; }

    public string? NewCustomCategoryName { get; set; }

    public string? ValidationError { get; private set; }

    public string? PendingMappingValue { get; private set; }

    public string? PendingNewCustomCategoryName { get; private set; }

    public MutationFeedbackState SaveState { get; private set; }

    public bool IsBusy => SaveState == MutationFeedbackState.Saving;

    public void BeginCustomCategoryCreate()
    {
        SaveState = MutationFeedbackState.None;
        ValidationError = null;
        IsCreatingCustomCategory = true;
        NewCustomCategoryName ??= Summary.UpstreamCategoryName;
    }

    public void CancelCustomCategoryCreate()
    {
        IsCreatingCustomCategory = false;
        MappingValue = SavedMappingValue;
        NewCustomCategoryName = null;
        ValidationError = null;
        PendingMappingValue = null;
        PendingNewCustomCategoryName = null;
        SaveState = MutationFeedbackState.None;
    }

    public void BeginSave(string mappingValue, string? newCustomCategoryName)
    {
        PendingMappingValue = mappingValue;
        PendingNewCustomCategoryName = newCustomCategoryName;
        ValidationError = null;
        SaveState = MutationFeedbackState.Saving;
    }

    public void CommitMapping(string mappingValue)
    {
        SavedMappingValue = mappingValue;
        MappingValue = mappingValue;
        IsCreatingCustomCategory = false;
        NewCustomCategoryName = null;
        ValidationError = null;
        PendingMappingValue = null;
        PendingNewCustomCategoryName = null;
        SaveState = MutationFeedbackState.Saved;
    }

    public void ApplyPersistedMapping(CategoryMappingSelectionOption mappingSelection, AdminCustomCategory? customCategory)
    {
        var currentMappingSelection = mappingSelection.ToString();
        var isManuallyExcluded = mappingSelection == CategoryMappingSelectionOption.Disabled;

        Summary = Summary with
        {
            IsManuallyExcluded = isManuallyExcluded,
            CustomCategoryId = customCategory?.Id,
            CustomCategoryName = customCategory?.DisplayName,
            EffectiveDecision = isManuallyExcluded ? "Exclude" : Summary.RuleDecision,
            IsEffectivelyIncluded = !isManuallyExcluded && string.Equals(Summary.RuleDecision, "Include", StringComparison.OrdinalIgnoreCase),
            CurrentMappingSelection = currentMappingSelection
        };

        CommitMapping(mappingSelection switch
        {
            CategoryMappingSelectionOption.Disabled => "disabled",
            CategoryMappingSelectionOption.Original => "original",
            CategoryMappingSelectionOption.Custom when customCategory is not null => GetCustomMappingValue(customCategory.Id),
            _ => throw new InvalidOperationException("Custom category details are required for custom mappings.")
        });
    }

    public void Fail(string message)
    {
        ValidationError = message;
        SaveState = MutationFeedbackState.Failed;
    }

    public void ClearFeedback()
    {
        if (SaveState != MutationFeedbackState.Saving)
        {
            SaveState = MutationFeedbackState.None;
        }

        ValidationError = null;
    }

    public static string GetCustomMappingValue(int customCategoryId) => $"custom:{customCategoryId}";

    private static string GetMappingValue(AdminUpstreamCategory summary) => summary.CurrentMappingSelection switch
    {
        "Disabled" => "disabled",
        "Original" => "original",
        "Custom" when summary.CustomCategoryId is int customCategoryId => GetCustomMappingValue(customCategoryId),
        _ => "original"
    };
}

public sealed class RuleRowState(AdminCategoryRule summary)
{
    private CategoryRuleActionOption _savedAction = ParseAction(summary.Action);
    private CategoryRuleOperatorOption _savedOperator = ParseOperator(summary.Operator);
    private string _savedPattern = summary.Pattern;
    private bool _savedCaseSensitive = summary.CaseSensitive;
    private bool _savedIsEnabled = summary.IsEnabled;

    public int RuleId { get; } = summary.Id;

    public int Sequence { get; set; } = summary.Sequence;

    public CategoryRuleActionOption Action { get; set; } = ParseAction(summary.Action);

    public CategoryRuleOperatorOption Operator { get; set; } = ParseOperator(summary.Operator);

    public string Pattern { get; set; } = summary.Pattern;

    public bool CaseSensitive { get; set; } = summary.CaseSensitive;

    public bool IsEnabled { get; set; } = summary.IsEnabled;

    public bool IsEditing { get; private set; }

    public MutationFeedbackState SaveState { get; private set; }

    public string? Message { get; private set; }

    public bool IsBusy => SaveState == MutationFeedbackState.Saving;

    public bool CanRunSecondaryActions => !IsEditing && !IsBusy;

    public void BeginEdit()
    {
        Message = null;
        SaveState = MutationFeedbackState.None;
        IsEditing = true;
    }

    public void CancelEdit()
    {
        Action = _savedAction;
        Operator = _savedOperator;
        Pattern = _savedPattern;
        CaseSensitive = _savedCaseSensitive;
        IsEnabled = _savedIsEnabled;
        Message = null;
        SaveState = MutationFeedbackState.None;
        IsEditing = false;
    }

    public void BeginSave()
    {
        Message = null;
        SaveState = MutationFeedbackState.Saving;
    }

    public void CommitSave()
    {
        _savedAction = Action;
        _savedOperator = Operator;
        _savedPattern = Pattern;
        _savedCaseSensitive = CaseSensitive;
        _savedIsEnabled = IsEnabled;
        Message = "Saved.";
        SaveState = MutationFeedbackState.Saved;
        IsEditing = false;
    }

    public void Fail(string message)
    {
        Message = message;
        SaveState = MutationFeedbackState.Failed;
    }

    public void SetSavedEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        _savedIsEnabled = isEnabled;
    }

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

public sealed class RuleTesterState
{
    public string? CategoryName { get; set; }

    public MutationFeedbackState SaveState { get; set; }

    public string? ErrorMessage { get; set; }

    public AdminCategoryRulePreview? Preview { get; set; }

    public bool IsBusy => SaveState == MutationFeedbackState.Saving;
}

public sealed class CustomCategoryRowState(AdminCustomCategory summary)
{
    private string _savedDisplayName = summary.DisplayName;
    private int _usageCount = summary.UsageCount;

    public AdminCustomCategory Summary { get; private set; } = summary;

    public string DisplayName { get; set; } = summary.DisplayName;

    public int UsageCount => _usageCount;

    public bool IsEditing { get; private set; }

    public MutationFeedbackState SaveState { get; private set; }

    public string? Message { get; private set; }

    public bool IsBusy => SaveState == MutationFeedbackState.Saving;

    public void BeginEdit()
    {
        Message = null;
        SaveState = MutationFeedbackState.None;
        IsEditing = true;
    }

    public void CancelEdit()
    {
        DisplayName = _savedDisplayName;
        Message = null;
        SaveState = MutationFeedbackState.None;
        IsEditing = false;
    }

    public void BeginSave()
    {
        Message = null;
        SaveState = MutationFeedbackState.Saving;
    }

    public void CommitSave()
    {
        _savedDisplayName = DisplayName;
        Message = "Saved.";
        SaveState = MutationFeedbackState.Saved;
        IsEditing = false;
    }

    public void Fail(string message)
    {
        Message = message;
        SaveState = MutationFeedbackState.Failed;
    }

    public void SetUsageCount(int usageCount)
    {
        _usageCount = Math.Max(0, usageCount);
    }

    public void RefreshSummary(AdminCustomCategory summary)
    {
        Summary = summary;
        DisplayName = summary.DisplayName;
        _savedDisplayName = summary.DisplayName;
        _usageCount = summary.UsageCount;
    }
}

public sealed class CustomCategoryEditorState
{
    public string? DisplayName { get; set; }
}

public sealed class CustomCategoryUsageRowState(AdminCustomCategoryUsage summary)
{
    public AdminCustomCategoryUsage Summary { get; } = summary;

    public bool IsConfirmingUnlink { get; private set; }

    public bool IsBusy { get; private set; }

    public string? ErrorMessage { get; private set; }

    public void BeginConfirm()
    {
        IsConfirmingUnlink = true;
        ErrorMessage = null;
    }

    public void CancelConfirm()
    {
        IsConfirmingUnlink = false;
        IsBusy = false;
        ErrorMessage = null;
    }

    public void BeginUnlink()
    {
        IsBusy = true;
        ErrorMessage = null;
    }

    public void Fail(string message)
    {
        IsBusy = false;
        ErrorMessage = message;
    }
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

public enum CategoriesTab
{
    SourceCategories = 1,
    CategoryRules = 2,
    GlobalCustomCategories = 3
}

public enum MutationFeedbackState
{
    None = 0,
    Saving = 1,
    Saved = 2,
    Failed = 3
}

public enum CategoryMappingSelectionOption
{
    Disabled = 1,
    Original = 2,
    Custom = 3
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

public sealed record CategoryMappingChangeRequest(int UpstreamCategoryRecordId, string? MappingValue);

public sealed record AdminCategoryRuleUpdate(
    int SelectedSourceId,
    string SelectedContentType,
    string Action,
    string Operator,
    string? Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record AdminCategoryRuleOrderUpdate(
    int SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<int> OrderedRuleIds);

public sealed record RuleDropRequest(int DraggedRuleId, int TargetIndex);

public sealed record RuleToggleRequest(RuleRowState Rule, bool IsEnabled);

public sealed record AdminCategoryRulePreview(
    string CategoryName,
    string Decision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    string? MatchedRuleAction,
    string? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive);

public sealed record AdminCustomCategoryCreate(string SelectedContentType, string? DisplayName);

public sealed record AdminCustomCategoryUpdate(string SelectedContentType, string DisplayName);

public sealed record AdminCategoryMappingResponse(
    int SourceId,
    string ContentType,
    string MappingSelection,
    AdminCustomCategory? CustomCategory);

public sealed record AdminSourceDiscoveryCreate(
    string? Protocol,
    string? HostOrBaseUrl,
    int? Port,
    string? Username,
    string? Password);

public sealed record AdminSourceDiscoveryResult(
    int SourceId,
    int VodCategoryCount,
    int SeriesCategoryCount);

public sealed record AdminCustomCategoryUsage(
    int UpstreamCategoryRecordId,
    int SourceId,
    string SourceProtocol,
    string SourceHost,
    int SourcePort,
    string ContentType,
    string UpstreamCategoryId,
    string UpstreamCategoryName)
{
    public string SourceDisplayName => $"{SourceProtocol}://{SourceHost}:{SourcePort}";
}

internal sealed record AdminErrorResponse(string Message);
