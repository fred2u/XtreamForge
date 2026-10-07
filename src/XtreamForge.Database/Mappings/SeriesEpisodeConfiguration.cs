using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain.Items;

namespace XtreamForge.Database.Mappings;

public class SeriesEpisodeConfiguration : IEntityTypeConfiguration<SeriesEpisode>
{
    public void Configure(EntityTypeBuilder<SeriesEpisode> seriesEpisodes)
    {
        seriesEpisodes.ToTable("series_episodes");

        seriesEpisodes.HasKey(episode => episode.Id);

        // an episode ID identifies one episode of a source
        seriesEpisodes.HasIndex(episode => new { episode.XtreamSourceId, episode.EpisodeId }).IsUnique();

        seriesEpisodes.Property(episode => episode.Id).HasColumnName("id");
        seriesEpisodes.Property(episode => episode.XtreamSourceId).HasColumnName("xtream_source_id");
        seriesEpisodes.Property(episode => episode.EpisodeId).HasColumnName("episode_id").HasMaxLength(64).IsRequired();
        seriesEpisodes.Property(episode => episode.SeriesId).HasColumnName("series_id").HasMaxLength(64).IsRequired();
        seriesEpisodes.Property(episode => episode.SeasonNumber).HasColumnName("season_number");
        seriesEpisodes.Property(episode => episode.EpisodeNumber).HasColumnName("episode_number");
        seriesEpisodes.Property(episode => episode.UpdatedAtUtc).HasColumnName("updated_at_utc");

        seriesEpisodes.HasOne(episode => episode.XtreamSource)
            .WithMany(source => source.SeriesEpisodes)
            .HasForeignKey(episode => episode.XtreamSourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
