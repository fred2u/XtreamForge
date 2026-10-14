using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XtreamForge.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceVirtualCategoryToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // the existing sources keep both virtual categories, like the new ones
            migrationBuilder.AddColumn<bool>(
                name: "is_popular_category_enabled",
                table: "xtream_sources",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_recommendations_category_enabled",
                table: "xtream_sources",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_popular_category_enabled",
                table: "xtream_sources");

            migrationBuilder.DropColumn(
                name: "is_recommendations_category_enabled",
                table: "xtream_sources");
        }
    }
}
