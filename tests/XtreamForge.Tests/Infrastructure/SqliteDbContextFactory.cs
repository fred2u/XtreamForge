using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XtreamForge.Database;

namespace XtreamForge.Tests.Infrastructure;

/// <summary>
/// Creates an <see cref="XtreamForgeDbContext"/> backed by a private SQLite in-memory database.
/// The database lives as long as the returned context (its connection) is not disposed.
/// <see cref="DateTimeOffset"/> values are stored as binary numbers ordered by their UTC instant, as PostgreSQL <c>timestamptz</c>:
/// SQLite cannot compare nor order the default text representation.
/// </summary>
public static class SqliteDbContextFactory
{
    public static XtreamForgeDbContext Create(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<XtreamForgeDbContext>()
            .UseSqlite("DataSource=:memory:")
            .ReplaceService<IModelCustomizer, SqliteModelCustomizer>()
            .AddInterceptors(interceptors)
            .Options;

        var dbContext = new XtreamForgeDbContext(options);
        dbContext.Database.OpenConnection();
        dbContext.Database.EnsureCreated();

        return dbContext;
    }

    /// <summary>Creates a second context on the database of <paramref name="dbContext"/>, as a concurrent request would.</summary>
    public static XtreamForgeDbContext CreateOnSameDatabase(DbContext dbContext)
    {
        var options = new DbContextOptionsBuilder<XtreamForgeDbContext>()
            .UseSqlite(dbContext.Database.GetDbConnection())
            .ReplaceService<IModelCustomizer, SqliteModelCustomizer>()
            .Options;

        return new XtreamForgeDbContext(options);
    }

    private sealed class SqliteModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            var dateTimeOffsetProperties = modelBuilder.Model.GetEntityTypes()
                .SelectMany(entityType => entityType.GetProperties())
                .Where(property => property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?));

            foreach (var property in dateTimeOffsetProperties)
            {
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            }
        }
    }
}
