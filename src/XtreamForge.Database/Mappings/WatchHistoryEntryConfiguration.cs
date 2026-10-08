using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain.History;

namespace XtreamForge.Database.Mappings;

public class WatchHistoryEntryConfiguration : IEntityTypeConfiguration<WatchHistoryEntry>
{
    public void Configure(EntityTypeBuilder<WatchHistoryEntry> watchHistory)
    {
        watchHistory.ToTable("watch_history");

        watchHistory.HasKey(entry => entry.Id);

        // recommendation seeds: the watched movies and series grouped by TMDB ID
        watchHistory.HasIndex(entry => new { entry.ContentType, entry.TmdbId });

        // activity: the playbacks of the last year
        watchHistory.HasIndex(entry => new { entry.ContentType, entry.StartedAtUtc });

        watchHistory.Property(entry => entry.Id).HasColumnName("id");
        watchHistory.Property(entry => entry.XtreamSourceId).HasColumnName("xtream_source_id");
        watchHistory.Property(entry => entry.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        watchHistory.Property(entry => entry.TmdbId).HasColumnName("tmdb_id");
        watchHistory.Property(entry => entry.SeasonNumber).HasColumnName("season_number");
        watchHistory.Property(entry => entry.EpisodeNumber).HasColumnName("episode_number");
        watchHistory.Property(entry => entry.StartedAtUtc).HasColumnName("started_at_utc");

        // the history outlives its sources: a deleted source only leaves its playbacks without source
        watchHistory.HasOne(entry => entry.XtreamSource)
            .WithMany()
            .HasForeignKey(entry => entry.XtreamSourceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
