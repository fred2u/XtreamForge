using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XtreamForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemRulesAndStreamTmdbMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "item_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xtream_source_id = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    field = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "character varying(20)", maxLength: 20, nullable: false),
                    pattern = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    case_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_item_rules_xtream_sources_xtream_source_id",
                        column: x => x.xtream_source_id,
                        principalTable: "xtream_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stream_tmdb_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xtream_source_id = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stream_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tmdb_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stream_tmdb_mappings", x => x.id);
                    table.ForeignKey(
                        name: "FK_stream_tmdb_mappings_xtream_sources_xtream_source_id",
                        column: x => x.xtream_source_id,
                        principalTable: "xtream_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_item_rules_xtream_source_id_content_type_is_enabled_sequence",
                table: "item_rules",
                columns: new[] { "xtream_source_id", "content_type", "is_enabled", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_item_rules_xtream_source_id_content_type_sequence",
                table: "item_rules",
                columns: new[] { "xtream_source_id", "content_type", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stream_tmdb_mappings_xtream_source_id_content_type",
                table: "stream_tmdb_mappings",
                columns: new[] { "xtream_source_id", "content_type" });

            migrationBuilder.CreateIndex(
                name: "IX_stream_tmdb_mappings_xtream_source_id_content_type_stream_id",
                table: "stream_tmdb_mappings",
                columns: new[] { "xtream_source_id", "content_type", "stream_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_rules");

            migrationBuilder.DropTable(
                name: "stream_tmdb_mappings");
        }
    }
}
