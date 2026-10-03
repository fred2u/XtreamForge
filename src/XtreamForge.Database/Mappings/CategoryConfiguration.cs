using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain.Categories;

namespace XtreamForge.Database.Mappings;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder
            .UseTptMappingStrategy()
            .ToTable("categories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Id).HasColumnName("id");
        builder.Property(category => category.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(category => category.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}
