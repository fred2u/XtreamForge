using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XtreamForge.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTmdbLookupRetries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "tmdb_id",
                table: "stream_tmdb_mappings",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<int>(
                name: "lookup_attempt_count",
                table: "stream_tmdb_mappings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_lookup_at_utc",
                table: "stream_tmdb_mappings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lookup_attempt_count",
                table: "stream_tmdb_mappings");

            migrationBuilder.DropColumn(
                name: "next_lookup_at_utc",
                table: "stream_tmdb_mappings");

            migrationBuilder.AlterColumn<long>(
                name: "tmdb_id",
                table: "stream_tmdb_mappings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
