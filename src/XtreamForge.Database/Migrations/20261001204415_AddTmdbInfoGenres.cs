using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XtreamForge.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTmdbInfoGenres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<int>>(
                name: "genre_ids",
                table: "tmdb_infos",
                type: "integer[]",
                nullable: false,
                // existing rows get an empty array
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "genres",
                table: "tmdb_infos",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "genre_ids",
                table: "tmdb_infos");

            migrationBuilder.DropColumn(
                name: "genres",
                table: "tmdb_infos");
        }
    }
}
