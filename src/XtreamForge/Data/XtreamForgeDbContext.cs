using Microsoft.EntityFrameworkCore;
using XtreamForge.Categories;

namespace XtreamForge.Data;

public sealed class XtreamForgeDbContext(DbContextOptions<XtreamForgeDbContext> options) : DbContext(options)
{
    public DbSet<XtreamSource> XtreamSources => Set<XtreamSource>();
    public DbSet<UpstreamCategory> UpstreamCategories => Set<UpstreamCategory>();
    public DbSet<OutputCategory> OutputCategories => Set<OutputCategory>();
    public DbSet<CustomCategory> CustomCategories => Set<CustomCategory>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var xtreamSources = modelBuilder.Entity<XtreamSource>();
        xtreamSources.ToTable("xtream_sources");
        xtreamSources.HasKey(source => source.Id);
        xtreamSources.HasIndex(source => new { source.Protocol, source.Host, source.Port }).IsUnique();
        xtreamSources.Property(source => source.Id).HasColumnName("id");
        xtreamSources.Property(source => source.Protocol).HasColumnName("protocol").HasMaxLength(10).IsRequired();
        xtreamSources.Property(source => source.Host).HasColumnName("host").HasMaxLength(255).IsRequired();
        xtreamSources.Property(source => source.Port).HasColumnName("port");
        xtreamSources.Property(source => source.FirstSeenAtUtc).HasColumnName("first_seen_at_utc");
        xtreamSources.Property(source => source.LastSeenAtUtc).HasColumnName("last_seen_at_utc");

        var outputCategories = modelBuilder.Entity<OutputCategory>();
        outputCategories.ToTable("output_categories");
        outputCategories.HasKey(category => category.Id);
        outputCategories.HasIndex(category => new { category.XtreamSourceId, category.ContentType, category.XtreamForgeCategoryId }).IsUnique();
        outputCategories.HasIndex(category => new { category.XtreamSourceId, category.ContentType, category.SortOrder });
        outputCategories.Property(category => category.Id).HasColumnName("id");
        outputCategories.Property(category => category.XtreamSourceId).HasColumnName("xtream_source_id");
        outputCategories.Property(category => category.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        outputCategories.Property(category => category.XtreamForgeCategoryId).HasColumnName("xtreamforge_category_id");
        outputCategories.Property(category => category.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
        outputCategories.Property(category => category.SortOrder).HasColumnName("sort_order");
        outputCategories.Property(category => category.CreatedAtUtc).HasColumnName("created_at_utc");
        outputCategories.Property(category => category.UpdatedAtUtc).HasColumnName("updated_at_utc");
        outputCategories.HasOne(category => category.XtreamSource)
            .WithMany(source => source.OutputCategories)
            .HasForeignKey(category => category.XtreamSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        var customCategories = modelBuilder.Entity<CustomCategory>();
        customCategories.ToTable("custom_categories");
        customCategories.HasKey(category => category.Id);
        customCategories.HasIndex(category => new { category.ContentType, category.XtreamForgeCategoryId }).IsUnique();
        customCategories.HasIndex(category => new { category.ContentType, category.NormalizedDisplayName }).IsUnique();
        customCategories.Property(category => category.Id).HasColumnName("id");
        customCategories.Property(category => category.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        customCategories.Property(category => category.XtreamForgeCategoryId).HasColumnName("xtreamforge_category_id");
        customCategories.Property(category => category.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
        customCategories.Property(category => category.NormalizedDisplayName).HasColumnName("normalized_display_name").HasMaxLength(255);
        customCategories.Property(category => category.CreatedAtUtc).HasColumnName("created_at_utc");
        customCategories.Property(category => category.UpdatedAtUtc).HasColumnName("updated_at_utc");

        var upstreamCategories = modelBuilder.Entity<UpstreamCategory>();
        upstreamCategories.ToTable("upstream_categories");
        upstreamCategories.HasKey(category => category.Id);
        upstreamCategories.HasIndex(category => new { category.XtreamSourceId, category.ContentType, category.UpstreamCategoryId }).IsUnique();
        upstreamCategories.HasIndex(category => new { category.XtreamSourceId, category.ContentType, category.CustomCategoryId });
        upstreamCategories.Property(category => category.Id).HasColumnName("id");
        upstreamCategories.Property(category => category.XtreamSourceId).HasColumnName("xtream_source_id");
        upstreamCategories.Property(category => category.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        upstreamCategories.Property(category => category.UpstreamCategoryId).HasColumnName("upstream_category_id").HasMaxLength(64).IsRequired();
        upstreamCategories.Property(category => category.UpstreamCategoryName).HasColumnName("upstream_category_name").HasMaxLength(255).IsRequired();
        upstreamCategories.Property(category => category.DedicatedOutputCategoryId).HasColumnName("dedicated_output_category_id");
        upstreamCategories.Property(category => category.CustomCategoryId).HasColumnName("custom_category_id");
        upstreamCategories.Property(category => category.IsExcluded).HasColumnName("is_excluded");
        upstreamCategories.Property(category => category.FirstDiscoveredAtUtc).HasColumnName("first_discovered_at_utc");
        upstreamCategories.Property(category => category.LastDiscoveredAtUtc).HasColumnName("last_discovered_at_utc");
        upstreamCategories.HasOne(category => category.XtreamSource)
            .WithMany(source => source.UpstreamCategories)
            .HasForeignKey(category => category.XtreamSourceId)
            .OnDelete(DeleteBehavior.Cascade);
        upstreamCategories.HasOne(category => category.DedicatedOutputCategory)
            .WithMany(category => category.DedicatedUpstreamCategories)
            .HasForeignKey(category => category.DedicatedOutputCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        upstreamCategories.HasOne(category => category.CustomCategory)
            .WithMany(category => category.UpstreamCategories)
            .HasForeignKey(category => category.CustomCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        var categoryRules = modelBuilder.Entity<CategoryRule>();
        categoryRules.ToTable("category_rules");
        categoryRules.HasKey(rule => rule.Id);
        categoryRules.HasIndex(rule => new { rule.XtreamSourceId, rule.ContentType, rule.Sequence }).IsUnique();
        categoryRules.HasIndex(rule => new { rule.XtreamSourceId, rule.ContentType, rule.IsEnabled, rule.Sequence });
        categoryRules.Property(rule => rule.Id).HasColumnName("id");
        categoryRules.Property(rule => rule.XtreamSourceId).HasColumnName("xtream_source_id");
        categoryRules.Property(rule => rule.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        categoryRules.Property(rule => rule.Sequence).HasColumnName("sequence");
        categoryRules.Property(rule => rule.Action).HasColumnName("action").HasConversion<string>().HasMaxLength(20);
        categoryRules.Property(rule => rule.Operator).HasColumnName("operator").HasConversion<string>().HasMaxLength(20);
        categoryRules.Property(rule => rule.Pattern).HasColumnName("pattern").HasMaxLength(255).IsRequired();
        categoryRules.Property(rule => rule.CaseSensitive).HasColumnName("case_sensitive");
        categoryRules.Property(rule => rule.IsEnabled).HasColumnName("is_enabled");
        categoryRules.Property(rule => rule.CreatedAtUtc).HasColumnName("created_at_utc");
        categoryRules.Property(rule => rule.UpdatedAtUtc).HasColumnName("updated_at_utc");
        categoryRules.HasOne(rule => rule.XtreamSource)
            .WithMany(source => source.CategoryRules)
            .HasForeignKey(rule => rule.XtreamSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        var settings = modelBuilder.Entity<Setting>();

        settings.ToTable("settings");
        settings.HasKey(setting => setting.Id);
        settings.HasIndex(setting => setting.Key).IsUnique();
        settings.Property(setting => setting.Id).HasColumnName("id");
        settings.Property(setting => setting.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
        settings.Property(setting => setting.Value).HasColumnName("value").HasMaxLength(4000);
        settings.Property(setting => setting.Description).HasColumnName("description").HasMaxLength(500);
        settings.Property(setting => setting.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}
