using Microsoft.EntityFrameworkCore;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.History;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.Database;

public sealed class XtreamForgeDbContext(DbContextOptions<XtreamForgeDbContext> options) : DbContext(options)
{
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<CustomCategory> CustomCategories => Set<CustomCategory>();
    public DbSet<ItemRule> ItemRules => Set<ItemRule>();
    public DbSet<StreamTmdbMapping> StreamTmdbMappings => Set<StreamTmdbMapping>();
    public DbSet<TmdbInfo> TmdbInfos => Set<TmdbInfo>();
    public DbSet<TmdbRule> TmdbRules => Set<TmdbRule>();
    public DbSet<WatchHistoryEntry> WatchHistory => Set<WatchHistoryEntry>();
    public DbSet<XtreamCategory> XtreamCategories => Set<XtreamCategory>();
    public DbSet<XtreamSource> XtreamSources => Set<XtreamSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseIdentityByDefaultColumns();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(XtreamForgeDbContext).Assembly);
    }
}
