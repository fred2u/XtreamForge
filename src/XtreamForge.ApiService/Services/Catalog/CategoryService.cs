using Microsoft.EntityFrameworkCore;
using System.Globalization;
using XtreamForge.ApiService.Endpoints.Xtream.Dto;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Catalog;

/// <summary>
/// Synchronizes the provider categories and resolves the requested categories. Provider category IDs (<c>xtream_id</c>) are compared
/// ordinally (case-sensitive) in memory, like the unique index of the database.
/// </summary>
public class CategoryService(XtreamForgeDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<XtreamCategoryDto>> SyncCategoriesAsync(XtreamContext xtreamContext, IReadOnlyCollection<XtreamCategoryDto> categories, CancellationToken cancellationToken)
    {
        var source = await SyncCategoriesAsync(xtreamContext.Protocol, xtreamContext.Host, xtreamContext.Port, xtreamContext.ContentType, categories, cancellationToken);

        // apply category rules
        var filteredCategories = RuleEvaluator.ApplyCategoryRules(source.XtreamCategories, source.CategoryRules);

        return [.. filteredCategories.Select(c => c.CustomCategory is null ? new XtreamCategoryDto(c.Id.ToString(), c.Name) : new XtreamCategoryDto(c.CustomCategory.Id.ToString(), c.CustomCategory.Name)).Distinct()];
    }

    public async Task<Domain.Sources.XtreamSource> SyncCategoriesAsync(string protocol, string host, int port, ContentType contentType, IReadOnlyCollection<XtreamCategoryDto> categories, CancellationToken cancellationToken)
    {
        try
        {
            return await SyncCategoriesOnceAsync(protocol, host, port, contentType, categories, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // a concurrent synchronization inserted the source or a category first (clients often request the VOD and series
            // categories in parallel on startup), so the unique indexes rejected the insert: the second attempt reads and updates them
            dbContext.ChangeTracker.Clear();

            return await SyncCategoriesOnceAsync(protocol, host, port, contentType, categories, cancellationToken);
        }
    }

    private async Task<Domain.Sources.XtreamSource> SyncCategoriesOnceAsync(string protocol, string host, int port, ContentType contentType, IReadOnlyCollection<XtreamCategoryDto> categories, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var source = await dbContext.XtreamSources
            .Include(s => s.XtreamCategories.Where(c => c.ContentType == contentType))
            .ThenInclude(c => c.CustomCategory)
            .Include(s => s.CategoryRules.Where(c => c.ContentType == contentType && c.IsEnabled))
            .FirstOrDefaultAsync(s => s.Protocol == protocol && s.Host == host && s.Port == port, cancellationToken);

        if (source is null)
        {
            // new source
            source = new Domain.Sources.XtreamSource
            {
                Protocol = protocol,
                Host = host,
                Port = port,
                XtreamCategories = [.. categories.Select(c => new Domain.Categories.XtreamCategory
                {
                    XtreamId = c.CategoryId,
                    Name = c.CategoryName,
                    ContentType = contentType,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                })]
            };
            dbContext.XtreamSources.Add(source);
        }
        else
        {
            // sync categories
            var existingCategories = source.XtreamCategories.ToDictionary(c => c.XtreamId, StringComparer.Ordinal);
            foreach (var categoryDto in categories)
            {
                if (existingCategories.TryGetValue(categoryDto.CategoryId, out var existingCategory))
                {
                    // update existing if necessary
                    if (!existingCategory.Name.Equals(categoryDto.CategoryName, StringComparison.OrdinalIgnoreCase) || !existingCategory.IsEnabled)
                    {
                        existingCategory.Name = categoryDto.CategoryName;
                        existingCategory.IsEnabled = true;
                        existingCategory.UpdatedAtUtc = now;

                        // intentionally reset the custom mapping when the upstream category is renamed or re-enabled:
                        // its content may have changed, so the admin must confirm the mapping again
                        existingCategory.CustomCategoryId = null;
                        existingCategory.CustomCategory = null;
                    }
                }
                else
                {
                    // add new category
                    var newCategory = new Domain.Categories.XtreamCategory
                    {
                        XtreamId = categoryDto.CategoryId,
                        Name = categoryDto.CategoryName,
                        ContentType = contentType,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };
                    source.XtreamCategories.Add(newCategory);
                }
            }

            // disable categories that are no longer present in the upstream source
            var upstreamCategoryIds = categories.Select(c => c.CategoryId).ToHashSet(StringComparer.Ordinal);
            foreach (var existingCategory in source.XtreamCategories.Where(existingCategory => !upstreamCategoryIds.Contains(existingCategory.XtreamId)))
            {
                if (existingCategory.IsEnabled)
                {
                    existingCategory.CustomCategoryId = null;
                    existingCategory.CustomCategory = null;
                    existingCategory.IsEnabled = false;
                    existingCategory.UpdatedAtUtc = now;
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        return source;
    }

    public static bool IsGetAll(XtreamContext xtreamContext)
    {
        var requestedCategoryId = xtreamContext.Request.Query["category_id"].ToString();
        return IsGetAll(requestedCategoryId);
    }

    /// <summary>
    /// Maps the upstream category IDs included for the requested <c>category_id</c> to their XtreamForge category ID
    /// (custom category ID when mapped, otherwise the Xtream category ID); empty when nothing matches.
    /// </summary>
    public async Task<Dictionary<string, int>> GetXtreamCategoryIdMappingAsync(XtreamContext xtreamContext, XtreamSourceSnapshot source, CancellationToken cancellationToken)
        => await GetXtreamCategoryIdMappingAsync(xtreamContext, source, xtreamContext.Request.Query["category_id"].ToString(), cancellationToken);

    /// <summary>
    /// Maps every included upstream category ID to its XtreamForge category ID, whatever the requested <c>category_id</c>
    /// (used for the recommendations category, filled from all the categories).
    /// </summary>
    public async Task<Dictionary<string, int>> GetAllXtreamCategoryIdMappingAsync(XtreamContext xtreamContext, XtreamSourceSnapshot source, CancellationToken cancellationToken)
        => await GetXtreamCategoryIdMappingAsync(xtreamContext, source, requestedCategoryId: null, cancellationToken);

    private async Task<Dictionary<string, int>> GetXtreamCategoryIdMappingAsync(XtreamContext xtreamContext, XtreamSourceSnapshot source, string? requestedCategoryId, CancellationToken cancellationToken)
    {
        List<Domain.Categories.XtreamCategory> xtreamCategories;
        if (IsGetAll(requestedCategoryId))
        {
            xtreamCategories = await dbContext.XtreamCategories
                .AsNoTracking()
                .Include(c => c.CustomCategory)
                .Where(c => c.XtreamSourceId == source.Id && c.ContentType == xtreamContext.ContentType && !c.IsExcluded && c.IsEnabled)
                .ToListAsync(cancellationToken);
        }
        else
        {
            // an invalid category_id matches no category, which the endpoint reports as a bad request
            if (!int.TryParse(requestedCategoryId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCategoryId))
                return [];

            xtreamCategories = await dbContext.XtreamCategories
               .AsNoTracking()
               .Include(c => c.CustomCategory)
               .Where(c => c.XtreamSourceId == source.Id && c.ContentType == xtreamContext.ContentType && !c.IsExcluded && c.IsEnabled)
               .Where(c => c.Id == parsedCategoryId || (c.CustomCategory != null && c.CustomCategory.Id == parsedCategoryId))
               .ToListAsync(cancellationToken);
        }

        // apply category rules
        var filteredCategories = RuleEvaluator.ApplyCategoryRules(xtreamCategories, source.CategoryRules);

        return BuildXtreamCategoryIdMapping(filteredCategories);
    }

    private static bool IsGetAll(string? requestedCategoryId)
    {
        return string.IsNullOrEmpty(requestedCategoryId) || requestedCategoryId.Equals("all", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, int> BuildXtreamCategoryIdMapping(IEnumerable<Domain.Categories.XtreamCategory> xtreamCategories)
    {
        var mapping = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var xtreamCategory in xtreamCategories)
        {
            var categoryId = xtreamCategory.CustomCategory is not null ? xtreamCategory.CustomCategory.Id : xtreamCategory.Id;
            mapping[xtreamCategory.XtreamId] = categoryId;
        }
        return mapping;
    }
}
