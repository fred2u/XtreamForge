using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace XtreamForge.Web.Features.Categories;

/// <summary>Display texts shared by the category screens, so that every screen uses the same terms.</summary>
public static class CategoryLabels
{
    public static IReadOnlyList<ContentType> ContentTypes { get; } = [ContentType.Vod, ContentType.Series];

    public static string ContentTypeName(ContentType contentType) => contentType switch
    {
        ContentType.Vod => "VOD",
        ContentType.Series => "Series",
        _ => contentType.ToString()
    };

    public static Icon ContentTypeIcon(ContentType contentType) =>
        contentType == ContentType.Vod ? new Icons.Regular.Size16.MoviesAndTv() : new Icons.Regular.Size16.Video();

    public static string ContentTypeCssClass(ContentType contentType) =>
        contentType == ContentType.Vod ? "type-pill-vod" : "type-pill-series";

    public static string ActionName(RuleAction action) => action switch
    {
        RuleAction.Include => "Include",
        RuleAction.Exclude => "Exclude",
        _ => action.ToString()
    };

    public static string OperatorName(RuleOperator @operator) => @operator switch
    {
        RuleOperator.StartsWith => "Starts with",
        RuleOperator.Contains => "Contains",
        RuleOperator.NotContains => "Does not contain",
        RuleOperator.NotStartsWith => "Does not start with",
        _ => @operator.ToString()
    };

    public static string ExclusionReasonName(CategoryExclusionReason reason) => reason switch
    {
        CategoryExclusionReason.ManuallyExcluded => "Manual exclusion",
        CategoryExclusionReason.ProviderDisabled => "Provider disabled",
        CategoryExclusionReason.Rule => "Category rule",
        _ => reason.ToString()
    };

    public static string ExclusionReasonDescription(CategoryExclusionReason reason) => reason switch
    {
        CategoryExclusionReason.ManuallyExcluded => "Excluded by an administrator. Category rules are not evaluated for this category.",
        CategoryExclusionReason.ProviderDisabled => "No longer returned by the provider. Category rules are not evaluated for this category.",
        CategoryExclusionReason.Rule => "Excluded by the first matching enabled category rule.",
        _ => reason.ToString()
    };
}
