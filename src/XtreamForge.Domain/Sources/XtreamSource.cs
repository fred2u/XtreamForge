using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Items;

namespace XtreamForge.Domain.Sources;

public sealed class XtreamSource
{
    public int Id { get; set; }

    public required string Protocol { get; set; }

    public required string Host { get; set; }

    public int Port { get; set; }

    /// <summary>Whether the recommendations virtual category is returned to the clients of this source (VOD and series).</summary>
    public bool IsRecommendationsCategoryEnabled { get; set; } = true;

    /// <summary>Whether the popular virtual category is returned to the clients of this source (VOD and series).</summary>
    public bool IsPopularCategoryEnabled { get; set; } = true;

    public List<XtreamCategory> XtreamCategories { get; set; } = [];

    public List<CategoryRule> CategoryRules { get; set; } = [];

    public List<ItemRule> ItemRules { get; set; } = [];

    public List<StreamTmdbMapping> StreamTmdbMappings { get; set; } = [];

    public List<SeriesEpisode> SeriesEpisodes { get; set; } = [];
}
