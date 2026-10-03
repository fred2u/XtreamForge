using System.Globalization;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Xtream.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services;

/// <summary>Category filled from a set of TMDB IDs instead of the provider categories (recommendations, popular).</summary>
public sealed record VirtualCategory(int CategoryId, IReadOnlySet<long> TmdbIds)
{
    public bool Contains(long? tmdbId) => tmdbId is { } id && TmdbIds.Contains(id);
}

/// <summary>
/// Virtual categories applied to the returned items. When a virtual category is <see cref="Requested"/>, only its items are returned,
/// in that category; otherwise an item is moved to the first of <see cref="Categories"/> (by priority) that contains it.
/// </summary>
public sealed record VirtualCategoryAssignment(IReadOnlyList<VirtualCategory> Categories, VirtualCategory? Requested = null)
{
    public bool Includes(long? tmdbId) => Requested is null || Requested.Contains(tmdbId);

    public VirtualCategory? CategoryOf(long? tmdbId)
    {
        if (Requested is not null)
            return Requested.Contains(tmdbId) ? Requested : null;

        return Categories.FirstOrDefault(category => category.Contains(tmdbId));
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
            ? await GetItemAssignmentAsync(xtreamContext.ContentType, cancellationToken)
            : null;
    }

    /// <summary>Returns every virtual category of the content type, to move an item (<c>get_vod_info</c> / <c>get_series_info</c>) to its virtual category.</summary>
    public async Task<VirtualCategoryAssignment?> GetItemAssignmentAsync(ContentType contentType, CancellationToken cancellationToken)
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
