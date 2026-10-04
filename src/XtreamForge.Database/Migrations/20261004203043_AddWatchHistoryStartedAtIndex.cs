using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XtreamForge.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchHistoryStartedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_watch_history_content_type_started_at_utc",
                table: "watch_history",
                columns: new[] { "content_type", "started_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_watch_history_content_type_started_at_utc",
                table: "watch_history");
        }
    }
}
