using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain.Categories;

namespace XtreamForge.Database.Mappings;

public class XtreamCategoryConfiguration : IEntityTypeConfiguration<XtreamCategory>
{
    public void Configure(EntityTypeBuilder<XtreamCategory> builder)
    {
        builder.ToTable("xtream_categories");

        builder.HasIndex(category => new { category.XtreamSourceId, category.ContentType, category.XtreamId }).IsUnique();
        builder.HasIndex(category => new { category.XtreamSourceId, category.ContentType, category.CustomCategoryId });

        builder.Property(category => category.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(category => category.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(category => category.XtreamId).HasColumnName("xtream_id").HasMaxLength(64).IsRequired();
        builder.Property(category => category.IsExcluded).HasColumnName("is_excluded");
        builder.Property(category => category.XtreamSourceId).HasColumnName("xtream_source_id");
        builder.Property(category => category.CustomCategoryId).HasColumnName("custom_category_id");

        builder.HasOne(category => category.XtreamSource)
            .WithMany(source => source.XtreamCategories)
            .HasForeignKey(category => category.XtreamSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(category => category.CustomCategory)
            .WithMany(category => category.XtreamCategories)
            .HasForeignKey(category => category.CustomCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
