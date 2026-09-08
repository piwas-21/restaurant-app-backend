using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuDisplaySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "menu_layout",
                table: "RestaurantInfo",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Tabs");

            migrationBuilder.AddColumn<bool>(
                name: "show_menu_bundles_on_all_tab",
                table: "RestaurantInfo",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "menu_layout",
                table: "RestaurantInfo");

            migrationBuilder.DropColumn(
                name: "show_menu_bundles_on_all_tab",
                table: "RestaurantInfo");
        }
    }
}
