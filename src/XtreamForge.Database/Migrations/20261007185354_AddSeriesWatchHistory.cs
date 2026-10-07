using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XtreamForge.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesWatchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "episode_number",
                table: "watch_history",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "season_number",
                table: "watch_history",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "series_episodes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xtream_source_id = table.Column<int>(type: "integer", nullable: false),
                    episode_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    series_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    season_number = table.Column<int>(type: "integer", nullable: true),
                    episode_number = table.Column<int>(type: "integer", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_episodes", x => x.id);
                    table.ForeignKey(
                        name: "FK_series_episodes_xtream_sources_xtream_source_id",
                        column: x => x.xtream_source_id,
                        principalTable: "xtream_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_series_episodes_xtream_source_id_episode_id",
                table: "series_episodes",
                columns: new[] { "xtream_source_id", "episode_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "series_episodes");

            migrationBuilder.DropColumn(
                name: "episode_number",
                table: "watch_history");

            migrationBuilder.DropColumn(
                name: "season_number",
                table: "watch_history");
        }
    }
}
