using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_quantity",
                table: "ProductIngredients",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ingredient_quantities_json",
                table: "OrderItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ingredient_quantities_json",
                table: "BasketItems",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_quantity",
                table: "ProductIngredients");

            migrationBuilder.DropColumn(
                name: "ingredient_quantities_json",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ingredient_quantities_json",
                table: "BasketItems");
        }
    }
}
