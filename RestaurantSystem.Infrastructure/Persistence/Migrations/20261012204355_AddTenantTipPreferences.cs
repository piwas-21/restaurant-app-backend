using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantTipPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tip_preset_currency",
                table: "RestaurantInfo",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tip_preset_mode",
                table: "RestaurantInfo",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "percentage");

            migrationBuilder.AddColumn<int[]>(
                name: "tip_preset_values",
                table: "RestaurantInfo",
                type: "integer[]",
                nullable: false,
                defaultValue: new[] { 10, 15, 20 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tip_preset_currency",
                table: "RestaurantInfo");

            migrationBuilder.DropColumn(
                name: "tip_preset_mode",
                table: "RestaurantInfo");

            migrationBuilder.DropColumn(
                name: "tip_preset_values",
                table: "RestaurantInfo");
        }
    }
}
