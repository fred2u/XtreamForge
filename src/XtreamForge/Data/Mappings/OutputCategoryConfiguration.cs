using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain;

namespace XtreamForge.Data.Mappings;

public class OutputCategoryConfiguration : IEntityTypeConfiguration<OutputCategory>
{
    public void Configure(EntityTypeBuilder<OutputCategory> builder)
    {
        builder.ToTable("output_categories");

        builder.HasKey(category => category.Id);

        builder.HasIndex(category => new { category.XtreamSourceId, category.ContentType, category.XtreamForgeCategoryId }).IsUnique();
        builder.HasIndex(category => new { category.XtreamSourceId, category.ContentType, category.SortOrder });

        builder.Property(category => category.Id).HasColumnName("id");
        builder.Property(category => category.XtreamSourceId).HasColumnName("xtream_source_id");
        builder.Property(category => category.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(category => category.XtreamForgeCategoryId).HasColumnName("xtreamforge_category_id");
        builder.Property(category => category.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
        builder.Property(category => category.SortOrder).HasColumnName("sort_order");
        builder.Property(category => category.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(category => category.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasOne(category => category.XtreamSource)
            .WithMany(source => source.OutputCategories)
            .HasForeignKey(category => category.XtreamSourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
