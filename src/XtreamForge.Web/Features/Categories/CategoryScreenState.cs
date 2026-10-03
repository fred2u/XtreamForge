namespace XtreamForge.Web.Features.Categories;

/// <summary>
/// Filters of the category screens, kept for the lifetime of the circuit so that they survive a navigation
/// to another screen and back. Registered as a scoped service.
/// </summary>
public sealed class CategoryScreenState
{
    public CategoryScope XtreamCategories { get; } = new();

    public XtreamCategoryFilter XtreamCategoriesFilter { get; set; } = new();

    public CategoryScope CategoryRules { get; } = new();

    public CategoryScope ItemRules { get; } = new();

    /// <summary>Scope of the TMDB rules: only the content type is used, TMDB rules have no source.</summary>
    public CategoryScope TmdbRules { get; } = new();

    public ContentType? CustomCategoriesContentType { get; set; }

    public string? CustomCategoriesSearch { get; set; }
}

/// <summary>Source and content type selected on a category screen.</summary>
public sealed class CategoryScope
{
    public int? SourceId { get; set; }

    public ContentType? ContentType { get; set; }

    /// <summary>
    /// Resolves the scope to show: the requested values (query string) win when valid, then the remembered values,
    /// and finally the first source and the first content type.
    /// </summary>
    public (int? SourceId, ContentType ContentType) Resolve(IReadOnlyList<int> sourceIds, int? requestedSourceId, ContentType? requestedContentType)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);

        int? sourceId = new[] { requestedSourceId, SourceId }.FirstOrDefault(id => id is { } candidate && sourceIds.Contains(candidate))
            ?? (sourceIds.Count > 0 ? sourceIds[0] : null);

        return (sourceId, requestedContentType ?? ContentType ?? CategoryLabels.ContentTypes[0]);
    }
}
