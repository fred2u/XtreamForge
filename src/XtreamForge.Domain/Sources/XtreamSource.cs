using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Items;

namespace XtreamForge.Domain.Sources;

public sealed class XtreamSource
{
    public int Id { get; set; }

    public required string Protocol { get; set; }

    public required string Host { get; set; }

    public int Port { get; set; }

    public List<XtreamCategory> XtreamCategories { get; set; } = [];

    public List<CategoryRule> CategoryRules { get; set; } = [];

    public List<ItemRule> ItemRules { get; set; } = [];

    public List<StreamTmdbMapping> StreamTmdbMappings { get; set; } = [];
}
