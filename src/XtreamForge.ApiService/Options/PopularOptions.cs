namespace XtreamForge.ApiService.Options;

/// <summary>Virtual VOD and series category of the movies and TV shows popular on TMDB.</summary>
public sealed class PopularOptions
{
    public const string SectionName = "Popular";

    /// <summary>Name of the category returned by <c>get_vod_categories</c> and <c>get_series_categories</c>; the category is disabled when empty.</summary>
    public string CategoryName { get; init; } = "Popular";

    /// <summary>XtreamForge ID of the category; it must not be the ID of an Xtream, custom, or recommendations category.</summary>
    public int CategoryId { get; init; } = 999_999_998;

    public bool IsCategoryEnabled => !string.IsNullOrWhiteSpace(CategoryName);
}
