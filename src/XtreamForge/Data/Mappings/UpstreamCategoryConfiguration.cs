using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain;

namespace XtreamForge.Data.Mappings;

public class UpstreamCategoryConfiguration : IEntityTypeConfiguration<UpstreamCategory>
{
    public void Configure(EntityTypeBuilder<UpstreamCategory> builder)
    {
        builder.ToTable("upstream_categories");

        builder.HasKey(category => category.Id);

        builder.HasIndex(category => new { category.XtreamSourceId, category.ContentType, category.UpstreamCategoryId }).IsUnique();
        builder.HasIndex(category => new { category.XtreamSourceId, category.ContentType, category.CustomCategoryId });

        builder.Property(category => category.Id).HasColumnName("id");
        builder.Property(category => category.XtreamSourceId).HasColumnName("xtream_source_id");
        builder.Property(category => category.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(category => category.UpstreamCategoryId).HasColumnName("upstream_category_id").HasMaxLength(64).IsRequired();
        builder.Property(category => category.UpstreamCategoryName).HasColumnName("upstream_category_name").HasMaxLength(255).IsRequired();
        builder.Property(category => category.DedicatedOutputCategoryId).HasColumnName("dedicated_output_category_id");
        builder.Property(category => category.CustomCategoryId).HasColumnName("custom_category_id");
        builder.Property(category => category.IsExcluded).HasColumnName("is_excluded");
        builder.Property(category => category.FirstDiscoveredAtUtc).HasColumnName("first_discovered_at_utc");
        builder.Property(category => category.LastDiscoveredAtUtc).HasColumnName("last_discovered_at_utc");

        builder.HasOne(category => category.XtreamSource)
            .WithMany(source => source.UpstreamCategories)
            .HasForeignKey(category => category.XtreamSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(category => category.DedicatedOutputCategory)
            .WithMany(category => category.DedicatedUpstreamCategories)
            .HasForeignKey(category => category.DedicatedOutputCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(category => category.CustomCategory)
            .WithMany(category => category.UpstreamCategories)
            .HasForeignKey(category => category.CustomCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
