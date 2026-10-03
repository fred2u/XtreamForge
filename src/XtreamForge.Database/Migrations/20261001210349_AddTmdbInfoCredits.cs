using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XtreamForge.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTmdbInfoCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "cast_members",
                table: "tmdb_infos",
                type: "text[]",
                nullable: false,
                // existing rows get an empty array
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "directors",
                table: "tmdb_infos",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int>(
                name: "duration_minutes",
                table: "tmdb_infos",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cast_members",
                table: "tmdb_infos");

            migrationBuilder.DropColumn(
                name: "directors",
                table: "tmdb_infos");

            migrationBuilder.DropColumn(
                name: "duration_minutes",
                table: "tmdb_infos");
        }
    }
}
