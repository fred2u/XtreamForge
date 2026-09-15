using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XtreamForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddXtreamCategoryManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "xtream_sources",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    protocol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_xtream_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "output_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xtream_source_id = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xtreamforge_category_id = table.Column<int>(type: "integer", nullable: false),
                    display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_name_customized = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_output_categories", x => x.id);
                    table.ForeignKey(
                        name: "FK_output_categories_xtream_sources_xtream_source_id",
                        column: x => x.xtream_source_id,
                        principalTable: "xtream_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "upstream_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xtream_source_id = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    upstream_category_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    upstream_category_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    output_category_id = table.Column<int>(type: "integer", nullable: true),
                    dedicated_output_category_id = table.Column<int>(type: "integer", nullable: false),
                    is_excluded = table.Column<bool>(type: "boolean", nullable: false),
                    first_discovered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_discovered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upstream_categories", x => x.id);
                    table.ForeignKey(
                        name: "FK_upstream_categories_output_categories_dedicated_output_cate~",
                        column: x => x.dedicated_output_category_id,
                        principalTable: "output_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_upstream_categories_output_categories_output_category_id",
                        column: x => x.output_category_id,
                        principalTable: "output_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_upstream_categories_xtream_sources_xtream_source_id",
                        column: x => x.xtream_source_id,
                        principalTable: "xtream_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_output_categories_xtream_source_id_content_type_sort_order",
                table: "output_categories",
                columns: new[] { "xtream_source_id", "content_type", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_output_categories_xtream_source_id_content_type_xtreamforge~",
                table: "output_categories",
                columns: new[] { "xtream_source_id", "content_type", "xtreamforge_category_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_upstream_categories_dedicated_output_category_id",
                table: "upstream_categories",
                column: "dedicated_output_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_upstream_categories_output_category_id",
                table: "upstream_categories",
                column: "output_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_upstream_categories_xtream_source_id_content_type_output_ca~",
                table: "upstream_categories",
                columns: new[] { "xtream_source_id", "content_type", "output_category_id" });

            migrationBuilder.CreateIndex(
                name: "IX_upstream_categories_xtream_source_id_content_type_upstream_~",
                table: "upstream_categories",
                columns: new[] { "xtream_source_id", "content_type", "upstream_category_id" },
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
                name: "upstream_categories");

            migrationBuilder.DropTable(
                name: "output_categories");

            migrationBuilder.DropTable(
                name: "xtream_sources");
        }
    }
}
