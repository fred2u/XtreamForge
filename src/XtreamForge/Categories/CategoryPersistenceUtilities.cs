using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using XtreamForge.Data;

namespace XtreamForge.Categories;

internal static class CategoryPersistenceUtilities
{
    public static async Task<int> GetNextXtreamForgeCategoryIdAsync(
        XtreamForgeDbContext dbContext,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        var nextCategoryId = await dbContext.OutputCategories
            .Where(category => category.ContentType == contentType)
            .Select(category => (int?)category.XtreamForgeCategoryId)
            .Concat(
                dbContext.CustomCategories
                    .Where(category => category.ContentType == contentType)
                    .Select(category => (int?)category.XtreamForgeCategoryId))
            .MaxAsync(cancellationToken) ?? 0;

        return nextCategoryId + 1;
    }

    public static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        or SqliteException { SqliteExtendedErrorCode: 2067 }
        or SqliteException { SqliteErrorCode: 19 };
}
