using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientKindAndSauceGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sauce_included_free",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sauce_max",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sauce_min",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "kind",
                table: "ProductIngredients",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "kind",
                table: "global_ingredients",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sauce_included_free",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "sauce_max",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "sauce_min",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "ProductIngredients");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "global_ingredients");
        }
    }
}
