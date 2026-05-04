using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class deleteConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductSideItems_Products_side_item_product_id",
                table: "ProductSideItems");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSideItems_Products_side_item_product_id",
                table: "ProductSideItems",
                column: "side_item_product_id",
                principalTable: "Products",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductSideItems_Products_side_item_product_id",
                table: "ProductSideItems");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSideItems_Products_side_item_product_id",
                table: "ProductSideItems",
                column: "side_item_product_id",
                principalTable: "Products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
