using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XtreamForge.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "xtream_sources",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    protocol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_xtream_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "custom_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_categories", x => x.id);
                    table.ForeignKey(
                        name: "FK_custom_categories_categories_id",
                        column: x => x.id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "category_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xtream_source_id = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_category_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_category_rules_xtream_sources_xtream_source_id",
                        column: x => x.xtream_source_id,
                        principalTable: "xtream_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xtream_source_id = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "xtream_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    xtream_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_excluded = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    xtream_source_id = table.Column<int>(type: "integer", nullable: false),
                    custom_category_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_xtream_categories", x => x.id);
                    table.ForeignKey(
                        name: "FK_xtream_categories_categories_id",
                        column: x => x.id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_xtream_categories_custom_categories_custom_category_id",
                        column: x => x.custom_category_id,
                        principalTable: "custom_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_xtream_categories_xtream_sources_xtream_source_id",
                        column: x => x.xtream_source_id,
                        principalTable: "xtream_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_category_rules_xtream_source_id_content_type_sequence",
                table: "category_rules",
                columns: new[] { "xtream_source_id", "content_type", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_categories_content_type_name",
                table: "custom_categories",
                columns: new[] { "content_type", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_rules_xtream_source_id_content_type_sequence",
                table: "item_rules",
                columns: new[] { "xtream_source_id", "content_type", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stream_tmdb_mappings_xtream_source_id_content_type_stream_id",
                table: "stream_tmdb_mappings",
                columns: new[] { "xtream_source_id", "content_type", "stream_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_xtream_categories_custom_category_id",
                table: "xtream_categories",
                column: "custom_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_xtream_categories_xtream_source_id_content_type_custom_cate~",
                table: "xtream_categories",
                columns: new[] { "xtream_source_id", "content_type", "custom_category_id" });

            migrationBuilder.CreateIndex(
                name: "IX_xtream_categories_xtream_source_id_content_type_xtream_id",
                table: "xtream_categories",
                columns: new[] { "xtream_source_id", "content_type", "xtream_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_xtream_sources_protocol_host_port",
                table: "xtream_sources",
                columns: new[] { "protocol", "host", "port" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "category_rules");

            migrationBuilder.DropTable(
                name: "item_rules");

            migrationBuilder.DropTable(
                name: "stream_tmdb_mappings");

            migrationBuilder.DropTable(
                name: "xtream_categories");

            migrationBuilder.DropTable(
                name: "custom_categories");

            migrationBuilder.DropTable(
                name: "xtream_sources");

            migrationBuilder.DropTable(
                name: "categories");
        }
    }
}
