using System.Globalization;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Xtream.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Catalog;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.VirtualCategories;

/// <summary>Category filled from a set of TMDB IDs instead of the provider categories (recommendations, popular).</summary>
public sealed record VirtualCategory(int CategoryId, IReadOnlySet<long> TmdbIds)
{
    public bool Contains(long? tmdbId) => tmdbId is { } id && TmdbIds.Contains(id);
}

/// <summary>
/// Virtual categories applied to the items of one list response. When a virtual category is requested, only its items are returned,
/// in that category; otherwise an item is moved to the first of the categories (by priority) that contains it.
/// A TMDB ID is assigned once per response: the first item with it takes the virtual category, the next ones keep their provider
/// category (and are therefore not listed when the virtual category is requested).
/// </summary>
public sealed class VirtualCategoryAssignment(IReadOnlyList<VirtualCategory> categories, VirtualCategory? requested = null)
{
    private readonly HashSet<long> _assignedTmdbIds = [];

    public bool IsCategoryRequested => requested is not null;

    public bool Includes(long? tmdbId) => requested is null || requested.Contains(tmdbId);

    /// <summary>
    /// Returns the virtual category of an item and reserves its TMDB ID for it; null when the item keeps its provider category,
    /// including when an earlier item of the response already took the TMDB ID.
    /// </summary>
    public VirtualCategory? Assign(long? tmdbId)
    {
        VirtualCategory? virtualCategory;
        if (requested is not null)
            virtualCategory = requested.Contains(tmdbId) ? requested : null;
        else
            virtualCategory = categories.FirstOrDefault(category => category.Contains(tmdbId));

        return tmdbId is { } id && virtualCategory is not null && _assignedTmdbIds.Add(id) ? virtualCategory : null;
    }
}

/// <summary>
/// Exposes the virtual categories configured by <see cref="RecommendationOptions"/> (VOD only) and <see cref="PopularOptions"/> (VOD and series),
/// in priority order: a recommended movie that is also popular belongs to the recommendations.
/// </summary>
public class VirtualCategoryService(
    RecommendationService recommendationService,
    PopularService popularService,
    IOptions<RecommendationOptions> recommendationOptions,
    IOptions<PopularOptions> popularOptions)
{
    /// <summary>Virtual categories of a content type, returned first by <c>get_vod_categories</c> / <c>get_series_categories</c>.</summary>
    public IReadOnlyList<XtreamCategoryDto> GetCategories(ContentType contentType)
        => [.. GetDefinitions(contentType).Select(definition => new XtreamCategoryDto(definition.Id.ToString(CultureInfo.InvariantCulture), definition.Name))];

    /// <summary>True when the requested <c>category_id</c> is a virtual category, filled from all the provider categories.</summary>
    public bool IsVirtualCategoryRequested(XtreamContext xtreamContext) => GetRequestedDefinition(xtreamContext) is not null;

    /// <summary>
    /// Returns the virtual categories to apply to an item list: only the requested virtual category, or every virtual category
    /// when all the categories are requested; null for a provider category, whose items keep their category.
    /// </summary>
    public async Task<VirtualCategoryAssignment?> GetListAssignmentAsync(XtreamContext xtreamContext, CancellationToken cancellationToken)
    {
        if (GetRequestedDefinition(xtreamContext) is { } requested)
            return new VirtualCategoryAssignment([], await LoadAsync(requested, xtreamContext.ContentType, cancellationToken));

        return CategoryService.IsGetAll(xtreamContext)
            ? await GetAllAssignmentAsync(xtreamContext.ContentType, cancellationToken)
            : null;
    }

    // every virtual category of the content type, to move the items of the list of all the categories to their virtual category
    private async Task<VirtualCategoryAssignment?> GetAllAssignmentAsync(ContentType contentType, CancellationToken cancellationToken)
    {
        var definitions = GetDefinitions(contentType);
        if (definitions.Count == 0)
            return null;

        List<VirtualCategory> categories = [];
        foreach (var definition in definitions)
        {
            categories.Add(await LoadAsync(definition, contentType, cancellationToken));
        }

        return new VirtualCategoryAssignment(categories);
    }

    private Definition? GetRequestedDefinition(XtreamContext xtreamContext)
    {
        var requestedCategoryId = xtreamContext.Request.Query["category_id"].ToString();
        if (!int.TryParse(requestedCategoryId, NumberStyles.None, CultureInfo.InvariantCulture, out var categoryId))
            return null;

        return GetDefinitions(xtreamContext.ContentType).FirstOrDefault(definition => definition.Id == categoryId);
    }

    // in priority order; the recommendations are computed from the movie history only
    private List<Definition> GetDefinitions(ContentType contentType)
    {
        List<Definition> definitions = [];
        if (contentType == ContentType.Vod && recommendationOptions.Value.IsCategoryEnabled)
            definitions.Add(new Definition(Kind.Recommendations, recommendationOptions.Value.CategoryId, recommendationOptions.Value.CategoryName));

        if (contentType is ContentType.Vod or ContentType.Series && popularOptions.Value.IsCategoryEnabled)
            definitions.Add(new Definition(Kind.Popular, popularOptions.Value.CategoryId, popularOptions.Value.CategoryName));

        return definitions;
    }

    private async Task<VirtualCategory> LoadAsync(Definition definition, ContentType contentType, CancellationToken cancellationToken)
    {
        var tmdbIds = definition.Kind == Kind.Recommendations
            ? await recommendationService.GetRecommendedTmdbIdsAsync(cancellationToken)
            : await popularService.GetPopularTmdbIdsAsync(contentType, cancellationToken);

        return new VirtualCategory(definition.Id, tmdbIds);
    }

    private enum Kind
    {
        Recommendations,
        Popular
    }

    private sealed record Definition(Kind Kind, int Id, string Name);
}
