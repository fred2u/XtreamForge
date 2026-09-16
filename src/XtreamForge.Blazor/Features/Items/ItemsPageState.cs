using XtreamForge.Blazor.Features.Categories;

namespace XtreamForge.Blazor.Features.Items;

public sealed class ItemRuleRowState(AdminItemRule summary)
{
    private ItemRuleFieldOption _savedField = ParseField(summary.Field);
    private ItemRuleActionOption _savedAction = ParseAction(summary.Action);
    private ItemRuleOperatorOption _savedOperator = ParseOperator(summary.Operator);
    private string _savedPattern = summary.Pattern;
    private bool _savedCaseSensitive = summary.CaseSensitive;
    private bool _savedIsEnabled = summary.IsEnabled;

    public int RuleId { get; } = summary.Id;

    public int Sequence { get; set; } = summary.Sequence;

    public ItemRuleFieldOption Field { get; set; } = ParseField(summary.Field);

    public ItemRuleActionOption Action { get; set; } = ParseAction(summary.Action);

    public ItemRuleOperatorOption Operator { get; set; } = ParseOperator(summary.Operator);

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
        Field = _savedField;
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
        _savedField = Field;
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

    private static ItemRuleFieldOption ParseField(string value) =>
        Enum.TryParse<ItemRuleFieldOption>(value, true, out var field)
            ? field
            : ItemRuleFieldOption.Name;

    private static ItemRuleActionOption ParseAction(string value) =>
        Enum.TryParse<ItemRuleActionOption>(value, true, out var action)
            ? action
            : ItemRuleActionOption.Include;

    private static ItemRuleOperatorOption ParseOperator(string value) =>
        Enum.TryParse<ItemRuleOperatorOption>(value, true, out var itemRuleOperator)
            ? itemRuleOperator
            : ItemRuleOperatorOption.Contains;
}

public sealed class ItemRuleEditorState
{
    public ItemRuleFieldOption Field { get; set; } = ItemRuleFieldOption.Name;

    public ItemRuleActionOption Action { get; set; } = ItemRuleActionOption.Exclude;

    public ItemRuleOperatorOption Operator { get; set; } = ItemRuleOperatorOption.Contains;

    public string? Pattern { get; set; }

    public bool CaseSensitive { get; set; }

    public bool IsEnabled { get; set; } = true;
}

public sealed class ItemRuleTesterState
{
    public string? ItemName { get; set; }

    public MutationFeedbackState SaveState { get; set; }

    public string? ErrorMessage { get; set; }

    public AdminItemRulePreview? Preview { get; set; }

    public bool IsBusy => SaveState == MutationFeedbackState.Saving;
}

public enum ItemRuleFieldOption
{
    Name = 1
}

public enum ItemRuleActionOption
{
    Include = 1,
    Exclude = 2
}

public enum ItemRuleOperatorOption
{
    Contains = 1,
    StartsWith = 2
}

public enum ItemsTab
{
    ItemRules = 1
}

public sealed record AdminItemRulesPayload(
    IReadOnlyList<AdminSource> Sources,
    int? SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<AdminItemRule> Rules);

public sealed record AdminItemRule(
    int Id,
    int Sequence,
    string Field,
    string Action,
    string Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record AdminItemRuleUpdate(
    int SelectedSourceId,
    string SelectedContentType,
    string Field,
    string Action,
    string Operator,
    string? Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record AdminItemRuleOrderUpdate(
    int SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<int> OrderedRuleIds);

public sealed record ItemRuleDropRequest(int DraggedRuleId, int TargetIndex);

public sealed record ItemRuleToggleRequest(ItemRuleRowState Rule, bool IsEnabled);

public sealed record AdminItemRulePreview(
    string ItemName,
    string Decision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    string? MatchedRuleField,
    string? MatchedRuleAction,
    string? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive);
