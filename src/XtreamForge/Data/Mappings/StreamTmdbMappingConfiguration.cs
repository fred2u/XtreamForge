using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain;

namespace XtreamForge.Data.Mappings;

public class StreamTmdbMappingConfiguration : IEntityTypeConfiguration<StreamTmdbMapping>
{
    public void Configure(EntityTypeBuilder<StreamTmdbMapping> streamTmdbMappings)
    {
        streamTmdbMappings.ToTable("stream_tmdb_mappings");

        streamTmdbMappings.HasKey(mapping => mapping.Id);

        streamTmdbMappings.HasIndex(mapping => new { mapping.XtreamSourceId, mapping.ContentType, mapping.StreamId }).IsUnique();
        streamTmdbMappings.HasIndex(mapping => new { mapping.XtreamSourceId, mapping.ContentType });

        streamTmdbMappings.Property(mapping => mapping.Id).HasColumnName("id");
        streamTmdbMappings.Property(mapping => mapping.XtreamSourceId).HasColumnName("xtream_source_id");
        streamTmdbMappings.Property(mapping => mapping.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        streamTmdbMappings.Property(mapping => mapping.StreamId).HasColumnName("stream_id").HasMaxLength(64).IsRequired();
        streamTmdbMappings.Property(mapping => mapping.TmdbId).HasColumnName("tmdb_id");
        streamTmdbMappings.Property(mapping => mapping.CreatedAtUtc).HasColumnName("created_at_utc");
        streamTmdbMappings.Property(mapping => mapping.UpdatedAtUtc).HasColumnName("updated_at_utc");

        streamTmdbMappings.HasOne(mapping => mapping.XtreamSource)
            .WithMany(source => source.StreamTmdbMappings)
            .HasForeignKey(mapping => mapping.XtreamSourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
