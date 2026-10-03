using XtreamForge.Domain.Enums;

namespace XtreamForge.Domain.Categories;

public sealed class CustomCategory : Category
{
    public ContentType ContentType { get; set; }

    public required string Name { get; set; }

    public ICollection<XtreamCategory> XtreamCategories { get; set; } = [];
}
