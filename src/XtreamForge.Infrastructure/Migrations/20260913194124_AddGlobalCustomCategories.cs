using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XtreamForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalCustomCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_upstream_categories_output_categories_output_category_id",
                table: "upstream_categories");

            migrationBuilder.DropIndex(
                name: "IX_upstream_categories_output_category_id",
                table: "upstream_categories");

            migrationBuilder.DropIndex(
                name: "IX_upstream_categories_xtream_source_id_content_type_output_ca~",
                table: "upstream_categories");

            migrationBuilder.AddColumn<int>(
                name: "custom_category_id",
                table: "upstream_categories",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "custom_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xtreamforge_category_id = table.Column<int>(type: "integer", nullable: false),
                    display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    normalized_display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_categories", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_categories_content_type_normalized_display_name",
                table: "custom_categories",
                columns: new[] { "content_type", "normalized_display_name" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_categories_content_type_xtreamforge_category_id",
                table: "custom_categories",
                columns: new[] { "content_type", "xtreamforge_category_id" },
                unique: true);

            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    INSERT INTO custom_categories (id, content_type, xtreamforge_category_id, display_name, normalized_display_name, created_at_utc, updated_at_utc)
                    SELECT DISTINCT
                        oc.id,
                        oc.content_type,
                        oc.xtreamforge_category_id,
                        oc.display_name,
                        UPPER(BTRIM(oc.display_name)),
                        oc.created_at_utc,
                        oc.updated_at_utc
                    FROM output_categories oc
                    WHERE EXISTS (
                        SELECT 1
                        FROM upstream_categories uc
                        WHERE uc.output_category_id = oc.id
                          AND uc.output_category_id <> uc.dedicated_output_category_id)
                       OR EXISTS (
                        SELECT 1
                        FROM upstream_categories uc
                        WHERE uc.dedicated_output_category_id = oc.id
                          AND uc.output_category_id = uc.dedicated_output_category_id
                          AND oc.is_name_customized = TRUE);

                    UPDATE upstream_categories uc
                    SET custom_category_id = uc.output_category_id
                    WHERE uc.output_category_id IS NOT NULL
                      AND (
                          uc.output_category_id <> uc.dedicated_output_category_id
                          OR EXISTS (
                              SELECT 1
                              FROM output_categories oc
                              WHERE oc.id = uc.output_category_id
                                AND oc.is_name_customized = TRUE));

                    UPDATE output_categories oc
                    SET display_name = uc.upstream_category_name,
                        updated_at_utc = CURRENT_TIMESTAMP
                    FROM upstream_categories uc
                    WHERE uc.dedicated_output_category_id = oc.id;

                    SELECT setval(
                        pg_get_serial_sequence('custom_categories', 'id'),
                        COALESCE((SELECT MAX(id) FROM custom_categories), 1),
                        TRUE);
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    INSERT INTO custom_categories (id, content_type, xtreamforge_category_id, display_name, normalized_display_name, created_at_utc, updated_at_utc)
                    SELECT DISTINCT
                        oc.id,
                        oc.content_type,
                        oc.xtreamforge_category_id,
                        oc.display_name,
                        UPPER(TRIM(oc.display_name)),
                        oc.created_at_utc,
                        oc.updated_at_utc
                    FROM output_categories oc
                    WHERE EXISTS (
                        SELECT 1
                        FROM upstream_categories uc
                        WHERE uc.output_category_id = oc.id
                          AND uc.output_category_id <> uc.dedicated_output_category_id)
                       OR EXISTS (
                        SELECT 1
                        FROM upstream_categories uc
                        WHERE uc.dedicated_output_category_id = oc.id
                          AND uc.output_category_id = uc.dedicated_output_category_id
                          AND oc.is_name_customized = 1);

                    UPDATE upstream_categories
                    SET custom_category_id = output_category_id
                    WHERE output_category_id IS NOT NULL
                      AND (
                          output_category_id <> dedicated_output_category_id
                          OR EXISTS (
                              SELECT 1
                              FROM output_categories oc
                              WHERE oc.id = upstream_categories.output_category_id
                                AND oc.is_name_customized = 1));

                    UPDATE output_categories
                    SET display_name = (
                            SELECT uc.upstream_category_name
                            FROM upstream_categories uc
                            WHERE uc.dedicated_output_category_id = output_categories.id
                            LIMIT 1),
                        updated_at_utc = CURRENT_TIMESTAMP
                    WHERE EXISTS (
                        SELECT 1
                        FROM upstream_categories uc
                        WHERE uc.dedicated_output_category_id = output_categories.id);
                    """);
            }

            migrationBuilder.DropColumn(
                name: "output_category_id",
                table: "upstream_categories");

            migrationBuilder.DropColumn(
                name: "is_enabled",
                table: "output_categories");

            migrationBuilder.DropColumn(
                name: "is_name_customized",
                table: "output_categories");

            migrationBuilder.CreateIndex(
                name: "IX_upstream_categories_custom_category_id",
                table: "upstream_categories",
                column: "custom_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_upstream_categories_xtream_source_id_content_type_custom_ca~",
                table: "upstream_categories",
                columns: new[] { "xtream_source_id", "content_type", "custom_category_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_upstream_categories_custom_categories_custom_category_id",
                table: "upstream_categories",
                column: "custom_category_id",
                principalTable: "custom_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_upstream_categories_custom_categories_custom_category_id",
                table: "upstream_categories");

            migrationBuilder.DropTable(
                name: "custom_categories");

            migrationBuilder.DropIndex(
                name: "IX_upstream_categories_custom_category_id",
                table: "upstream_categories");

            migrationBuilder.DropIndex(
                name: "IX_upstream_categories_xtream_source_id_content_type_custom_ca~",
                table: "upstream_categories");

            migrationBuilder.AddColumn<int>(
                name: "output_category_id",
                table: "upstream_categories",
                type: "integer",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "custom_category_id",
                table: "upstream_categories");

            migrationBuilder.AddColumn<bool>(
                name: "is_enabled",
                table: "output_categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_name_customized",
                table: "output_categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_upstream_categories_output_category_id",
                table: "upstream_categories",
                column: "output_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_upstream_categories_xtream_source_id_content_type_output_ca~",
                table: "upstream_categories",
                columns: new[] { "xtream_source_id", "content_type", "output_category_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_upstream_categories_output_categories_output_category_id",
                table: "upstream_categories",
                column: "output_category_id",
                principalTable: "output_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
