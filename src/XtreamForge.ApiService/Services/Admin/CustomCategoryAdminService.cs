using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Admin;

public class CustomCategoryAdminService(XtreamForgeDbContext dbContext)
{
    public async Task<IReadOnlyList<CustomCategory>> GetAllAsync(
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.CustomCategories
            .AsNoTracking()
            .Where(c => c.ContentType == contentType)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<(CustomCategory? Category, bool NameConflict)> CreateAsync(
        string name,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        var nameExists = await dbContext.CustomCategories
            .AnyAsync(c => c.ContentType == contentType && c.Name == name, cancellationToken);

        if (nameExists)
        {
            return (null, true);
        }

        var category = new CustomCategory
        {
            Name = name,
            ContentType = contentType
        };

        dbContext.CustomCategories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (category, false);
    }

    public async Task<(bool Found, bool NameConflict)> UpdateAsync(
        int id,
        string name,
        CancellationToken cancellationToken = default)
    {
        var category = await dbContext.CustomCategories
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            return (false, false);
        }

        var nameExists = await dbContext.CustomCategories
            .AnyAsync(c => c.ContentType == category.ContentType && c.Name == name && c.Id != id, cancellationToken);

        if (nameExists)
        {
            return (true, true);
        }

        category.Name = name;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return (true, false);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await dbContext.CustomCategories
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            return false;
        }

        dbContext.CustomCategories.Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
