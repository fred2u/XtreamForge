using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XtreamForge.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchHistorySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "xtream_source_id",
                table: "watch_history",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_watch_history_xtream_source_id",
                table: "watch_history",
                column: "xtream_source_id");

            migrationBuilder.AddForeignKey(
                name: "FK_watch_history_xtream_sources_xtream_source_id",
                table: "watch_history",
                column: "xtream_source_id",
                principalTable: "xtream_sources",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_watch_history_xtream_sources_xtream_source_id",
                table: "watch_history");

            migrationBuilder.DropIndex(
                name: "IX_watch_history_xtream_source_id",
                table: "watch_history");

            migrationBuilder.DropColumn(
                name: "xtream_source_id",
                table: "watch_history");
        }
    }
}
