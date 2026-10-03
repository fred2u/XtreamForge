using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Admin;

public class XtreamCategoryAdminService(XtreamForgeDbContext dbContext)
{
    /// <summary>
    /// Returns the categories of a source and content type with the decision the runtime category filtering
    /// makes for them, evaluated with the rules of the same source and content type.
    /// </summary>
    public async Task<IReadOnlyList<CategoryRuleEvaluation>> GetBySourceAsync(
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        var categories = await dbContext.XtreamCategories
            .AsNoTracking()
            .Include(c => c.CustomCategory)
            .Where(c => c.XtreamSourceId == sourceId && c.ContentType == contentType)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var rules = await dbContext.CategoryRules
            .AsNoTracking()
            .Where(r => r.XtreamSourceId == sourceId && r.ContentType == contentType && r.IsEnabled)
            .ToListAsync(cancellationToken);

        return [.. CategoryRuleService.Evaluate(categories, rules)];
    }

    /// <summary>
    /// Updates IsExcluded and/or the custom category of a given XtreamCategory.
    /// A null <paramref name="isExcluded"/> or <paramref name="customCategoryId"/> leaves the value unchanged;
    /// <paramref name="unassignCustomCategory"/> removes the custom category.
    /// Nothing is saved when the category or the referenced custom category does not exist.
    /// </summary>
    public async Task<XtreamCategoryPatchResult> PatchAsync(
        int id,
        bool? isExcluded,
        int? customCategoryId,
        bool unassignCustomCategory,
        CancellationToken cancellationToken = default)
    {
        var category = await dbContext.XtreamCategories
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            return XtreamCategoryPatchResult.CategoryNotFound;
        }

        if (isExcluded.HasValue)
        {
            category.IsExcluded = isExcluded.Value;
        }

        if (unassignCustomCategory)
        {
            category.CustomCategoryId = null;
        }
        else if (customCategoryId.HasValue)
        {
            var exists = await dbContext.CustomCategories
                .AnyAsync(cc => cc.Id == customCategoryId.Value, cancellationToken);

            if (!exists)
            {
                return XtreamCategoryPatchResult.CustomCategoryNotFound;
            }

            category.CustomCategoryId = customCategoryId.Value;
        }

        category.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return XtreamCategoryPatchResult.Updated;
    }
}
