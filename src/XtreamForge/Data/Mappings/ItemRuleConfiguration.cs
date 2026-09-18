using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain;

namespace XtreamForge.Data.Mappings;

public class ItemRuleConfiguration : IEntityTypeConfiguration<ItemRule>
{
    public void Configure(EntityTypeBuilder<ItemRule> builder)
    {
        builder.ToTable("item_rules");

        builder.HasKey(rule => rule.Id);

        builder.HasIndex(rule => new { rule.XtreamSourceId, rule.ContentType, rule.Sequence }).IsUnique();
        builder.HasIndex(rule => new { rule.XtreamSourceId, rule.ContentType, rule.IsEnabled, rule.Sequence });

        builder.Property(rule => rule.Id).HasColumnName("id");
        builder.Property(rule => rule.XtreamSourceId).HasColumnName("xtream_source_id");
        builder.Property(rule => rule.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.Sequence).HasColumnName("sequence");
        builder.Property(rule => rule.Field).HasColumnName("field").HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.Action).HasColumnName("action").HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.Operator).HasColumnName("operator").HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.Pattern).HasColumnName("pattern").HasMaxLength(255).IsRequired();
        builder.Property(rule => rule.CaseSensitive).HasColumnName("case_sensitive");
        builder.Property(rule => rule.IsEnabled).HasColumnName("is_enabled");
        builder.Property(rule => rule.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(rule => rule.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasOne(rule => rule.XtreamSource)
            .WithMany(source => source.ItemRules)
            .HasForeignKey(rule => rule.XtreamSourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
