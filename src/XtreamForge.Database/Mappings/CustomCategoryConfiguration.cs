using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain.Categories;

namespace XtreamForge.Database.Mappings;

public class CustomCategoryConfiguration : IEntityTypeConfiguration<CustomCategory>
{
    public void Configure(EntityTypeBuilder<CustomCategory> builder)
    {
        builder.ToTable("custom_categories");

        builder.Property(category => category.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(category => category.Name).HasColumnName("name").HasMaxLength(255).IsRequired();

        builder.HasIndex(category => new { category.ContentType, category.Name }).IsUnique();
    }
}
