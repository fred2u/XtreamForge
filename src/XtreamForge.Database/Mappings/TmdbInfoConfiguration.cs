using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.Database.Mappings;

public class TmdbInfoConfiguration : IEntityTypeConfiguration<TmdbInfo>
{
    public void Configure(EntityTypeBuilder<TmdbInfo> tmdbInfos)
    {
        tmdbInfos.ToTable("tmdb_infos");

        tmdbInfos.HasKey(info => info.Id);

        tmdbInfos.HasIndex(info => new { info.ContentType, info.TmdbId }).IsUnique();

        tmdbInfos.Property(info => info.Id).HasColumnName("id");
        tmdbInfos.Property(info => info.TmdbId).HasColumnName("tmdb_id");
        tmdbInfos.Property(info => info.ContentType).HasColumnName("content_type").HasConversion<string>().HasMaxLength(20);
        tmdbInfos.Property(info => info.Title).HasColumnName("title").HasMaxLength(500);
        tmdbInfos.Property(info => info.OriginalTitle).HasColumnName("original_title").HasMaxLength(500);
        tmdbInfos.Property(info => info.ReleaseDate).HasColumnName("release_date");
        tmdbInfos.Property(info => info.PosterPath).HasColumnName("poster_path").HasMaxLength(500);
        tmdbInfos.Property(info => info.Overview).HasColumnName("overview");
        tmdbInfos.Property(info => info.VoteAverage).HasColumnName("vote_average");
        tmdbInfos.Property(info => info.VoteCount).HasColumnName("vote_count");
        tmdbInfos.Property(info => info.GenreIds).HasColumnName("genre_ids");
        tmdbInfos.Property(info => info.Genres).HasColumnName("genres");
        tmdbInfos.Property(info => info.Directors).HasColumnName("directors");
        // "cast" is a PostgreSQL reserved word
        tmdbInfos.Property(info => info.Cast).HasColumnName("cast_members");
        tmdbInfos.Property(info => info.DurationMinutes).HasColumnName("duration_minutes");
        tmdbInfos.Property(info => info.IsExcluded).HasColumnName("is_excluded");
        tmdbInfos.Property(info => info.LoadedAtUtc).HasColumnName("loaded_at_utc");
        tmdbInfos.Property(info => info.LoadAttemptCount).HasColumnName("load_attempt_count");
        tmdbInfos.Property(info => info.NextLoadAtUtc).HasColumnName("next_load_at_utc");
        tmdbInfos.Property(info => info.CreatedAtUtc).HasColumnName("created_at_utc");
        tmdbInfos.Property(info => info.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}
