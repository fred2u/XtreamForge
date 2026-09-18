using Microsoft.EntityFrameworkCore;
using XtreamForge.Domain;

namespace XtreamForge.Data;

public sealed class XtreamForgeDbContext(DbContextOptions<XtreamForgeDbContext> options) : DbContext(options)
{
    public DbSet<XtreamSource> XtreamSources => Set<XtreamSource>();
    public DbSet<UpstreamCategory> UpstreamCategories => Set<UpstreamCategory>();
    public DbSet<OutputCategory> OutputCategories => Set<OutputCategory>();
    public DbSet<CustomCategory> CustomCategories => Set<CustomCategory>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<ItemRule> ItemRules => Set<ItemRule>();
    public DbSet<StreamTmdbMapping> StreamTmdbMappings => Set<StreamTmdbMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(XtreamForgeDbContext).Assembly);
    }
}
