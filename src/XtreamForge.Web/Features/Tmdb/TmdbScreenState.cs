using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Web.Features.Tmdb;

/// <summary>
/// Scope and filters of the TMDB screens, kept for the lifetime of the circuit so that they survive a navigation
/// to another screen and back. Registered as a scoped service.
/// </summary>
public sealed class TmdbScreenState
{
    public ContentType? ContentType { get; set; }

    public TmdbInfoFilter Filter { get; set; } = new();

    /// <summary>Source and content type of the TMDB mappings screen (Items section).</summary>
    public CategoryScope Mappings { get; } = new();

    public TmdbMappingFilter MappingFilter { get; set; } = new();
}
