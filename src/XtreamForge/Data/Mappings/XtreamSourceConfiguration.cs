using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain;

namespace XtreamForge.Data.Mappings;

public class XtreamSourceConfiguration : IEntityTypeConfiguration<XtreamSource>
{
    public void Configure(EntityTypeBuilder<XtreamSource> builder)
    {
        builder.ToTable("xtream_sources");

        builder.HasKey(source => source.Id);

        builder.HasIndex(source => new { source.Protocol, source.Host, source.Port }).IsUnique();

        builder.Property(source => source.Id).HasColumnName("id");
        builder.Property(source => source.Protocol).HasColumnName("protocol").HasMaxLength(10).IsRequired();
        builder.Property(source => source.Host).HasColumnName("host").HasMaxLength(255).IsRequired();
        builder.Property(source => source.Port).HasColumnName("port");
        builder.Property(source => source.FirstSeenAtUtc).HasColumnName("first_seen_at_utc");
        builder.Property(source => source.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
    }
}
