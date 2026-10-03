using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Sources;

namespace XtreamForge.Domain.Categories;

public sealed class XtreamCategory : Category
{
    public ContentType ContentType { get; set; }

    public required string Name { get; set; }

    public required string XtreamId { get; set; }

    public bool IsExcluded { get; set; } // controlled in the admin portal

    public bool IsEnabled { get; set; } = true; // controlled by the provider

    public int XtreamSourceId { get; set; }
    public XtreamSource XtreamSource { get; set; } = null!;

    public int? CustomCategoryId { get; set; }
    public CustomCategory? CustomCategory { get; set; }
}
