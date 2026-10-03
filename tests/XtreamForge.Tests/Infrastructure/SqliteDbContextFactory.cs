using Microsoft.EntityFrameworkCore;
using XtreamForge.Database;

namespace XtreamForge.Tests.Infrastructure;

/// <summary>
/// Creates an <see cref="XtreamForgeDbContext"/> backed by a private SQLite in-memory database.
/// The database lives as long as the returned context (its connection) is not disposed.
/// </summary>
public static class SqliteDbContextFactory
{
    public static XtreamForgeDbContext Create()
    {
        var options = new DbContextOptionsBuilder<XtreamForgeDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var dbContext = new XtreamForgeDbContext(options);
        dbContext.Database.OpenConnection();
        dbContext.Database.EnsureCreated();

        return dbContext;
    }
}
