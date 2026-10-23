using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XtreamForge.Database.Migrations
{
    /// <inheritdoc />
    public partial class DeduplicateWatchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // a movie or episode now appears once for playbacks at most 36 hours apart (see WatchHistoryDeduplication): of each run of
            // playbacks of the same movie or episode with at most 36 hours between two consecutive ones, only the last one is kept
            migrationBuilder.Sql("""
                DELETE FROM watch_history AS entry
                USING (
                    SELECT id, started_at_utc, LEAD(started_at_utc) OVER (
                        PARTITION BY content_type, tmdb_id, season_number, episode_number
                        ORDER BY started_at_utc, id) AS next_started_at_utc
                    FROM watch_history
                ) AS playback
                WHERE entry.id = playback.id
                    AND playback.next_started_at_utc <= playback.started_at_utc + INTERVAL '36 hours';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the deleted playbacks cannot be restored
        }
    }
}
