namespace XtreamForge.Domain.Enums;

/// <summary>Why a category is excluded, in the order the checks are made.</summary>
public enum CategoryExclusionReason
{
    ManuallyExcluded = 1,
    ProviderDisabled = 2,
    Rule = 3
}
