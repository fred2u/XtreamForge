using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace XtreamForge.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<XtreamForgeDbContext>
{
    public XtreamForgeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__database")
            ?? "Host=localhost;Port=5432;Database=xtreamforge;Username=postgres;******";

        var optionsBuilder = new DbContextOptionsBuilder<XtreamForgeDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new XtreamForgeDbContext(optionsBuilder.Options);
    }
}
