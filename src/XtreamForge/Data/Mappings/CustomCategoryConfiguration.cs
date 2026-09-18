using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain;

namespace XtreamForge.Data.Mappings;

public class CustomCategoryConfiguration : IEntityTypeConfiguration<CustomCategory>
{
    public void Configure(EntityTypeBuilder<CustomCategory> builder)
    {
        builder.ToTable("custom_categories");

        builder.HasKey(category => category.Id);

        builder.HasIndex(category => new { category.ContentType, category.XtreamForgeCategoryId }).IsUnique();
        builder.HasIndex(category => new { category.ContentType, category.NormalizedDisplayName }).IsUnique();

        builder.Property(category => category.Id).HasColumnName("id");
        builder.Property(category => category.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(category => category.XtreamForgeCategoryId).HasColumnName("xtreamforge_category_id");
        builder.Property(category => category.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
        builder.Property(category => category.NormalizedDisplayName).HasColumnName("normalized_display_name").HasMaxLength(255);
        builder.Property(category => category.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(category => category.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}
