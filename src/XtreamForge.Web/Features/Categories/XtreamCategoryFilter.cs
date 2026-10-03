namespace XtreamForge.Web.Features.Categories;

public enum MappingFilter
{
    All,
    Mapped,
    Unmapped
}

public enum ManualExclusionFilter
{
    All,
    Excluded,
    NotExcluded
}

public enum DecisionFilter
{
    All,
    Included,
    Excluded
}

public enum ProviderStateFilter
{
    All,
    Enabled,
    Disabled
}

/// <summary>
/// Client-side filter of the Xtream categories of one source and content type (the scope is chosen before loading).
/// The decision filter uses the decision computed by the API; it does not evaluate rules.
/// </summary>
public sealed record XtreamCategoryFilter(
    string? Search = null,
    MappingFilter Mapping = MappingFilter.All,
    ManualExclusionFilter ManualExclusion = ManualExclusionFilter.All,
    DecisionFilter Decision = DecisionFilter.All,
    ProviderStateFilter ProviderState = ProviderStateFilter.All)
{
    public bool IsEmpty => this == new XtreamCategoryFilter();

    /// <summary>Search matches the category name, its Xtream id and the name of its custom category.</summary>
    public bool Matches(XtreamCategoryDto category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return MatchesSearch(category)
            && Mapping switch
            {
                MappingFilter.Mapped => category.CustomCategoryId is not null,
                MappingFilter.Unmapped => category.CustomCategoryId is null,
                _ => true
            }
            && ManualExclusion switch
            {
                ManualExclusionFilter.Excluded => category.IsExcluded,
                ManualExclusionFilter.NotExcluded => !category.IsExcluded,
                _ => true
            }
            && Decision switch
            {
                DecisionFilter.Included => category.Decision == InclusionDecision.Include,
                DecisionFilter.Excluded => category.Decision == InclusionDecision.Exclude,
                _ => true
            }
            && ProviderState switch
            {
                ProviderStateFilter.Enabled => category.IsEnabled,
                ProviderStateFilter.Disabled => !category.IsEnabled,
                _ => true
            };
    }

    private bool MatchesSearch(XtreamCategoryDto category)
    {
        var search = Search?.Trim();
        return string.IsNullOrEmpty(search)
            || category.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || category.XtreamId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (category.CustomCategoryName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
