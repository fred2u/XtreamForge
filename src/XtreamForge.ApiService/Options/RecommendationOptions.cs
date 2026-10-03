namespace XtreamForge.ApiService.Options;

/// <summary>Virtual VOD category of the movies recommended from the watch history.</summary>
public sealed class RecommendationOptions
{
    public const string SectionName = "Recommendations";

    /// <summary>Name of the category returned by <c>get_vod_categories</c>; the category is disabled when empty.</summary>
    public string CategoryName { get; init; } = "Recommendations";

    /// <summary>XtreamForge ID of the category; it must not be the ID of an Xtream or custom category.</summary>
    public int CategoryId { get; init; } = 999_999_999;

    public bool IsCategoryEnabled => !string.IsNullOrWhiteSpace(CategoryName);
}
