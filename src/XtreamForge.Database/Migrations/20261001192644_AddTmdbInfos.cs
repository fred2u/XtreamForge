using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XtreamForge.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTmdbInfos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tmdb_infos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tmdb_id = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    original_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    release_date = table.Column<DateOnly>(type: "date", nullable: true),
                    poster_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    overview = table.Column<string>(type: "text", nullable: true),
                    vote_average = table.Column<double>(type: "double precision", nullable: true),
                    vote_count = table.Column<int>(type: "integer", nullable: true),
                    loaded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    load_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_load_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tmdb_infos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tmdb_infos_content_type_tmdb_id",
                table: "tmdb_infos",
                columns: new[] { "content_type", "tmdb_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tmdb_infos");
        }
    }
}
