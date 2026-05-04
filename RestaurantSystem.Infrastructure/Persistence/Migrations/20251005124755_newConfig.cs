using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class newConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_product_side_items_Products_main_product_id",
                table: "product_side_items");

            migrationBuilder.DropForeignKey(
                name: "FK_product_side_items_Products_side_item_product_id",
                table: "product_side_items");

            migrationBuilder.RenameTable(
                name: "product_side_items",
                newName: "ProductSideItems");

            migrationBuilder.RenameIndex(
                name: "IX_product_side_items_side_item_product_id",
                table: "ProductSideItems",
                newName: "IX_ProductSideItems_side_item_product_id");

            migrationBuilder.RenameIndex(
                name: "IX_product_side_items_main_product_id",
                table: "ProductSideItems",
                newName: "IX_ProductSideItems_main_product_id");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSideItems_display_order",
                table: "ProductSideItems",
                column: "display_order");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSideItems_Products_main_product_id",
                table: "ProductSideItems",
                column: "main_product_id",
                principalTable: "Products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSideItems_Products_side_item_product_id",
                table: "ProductSideItems",
                column: "side_item_product_id",
                principalTable: "Products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductSideItems_Products_main_product_id",
                table: "ProductSideItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductSideItems_Products_side_item_product_id",
                table: "ProductSideItems");

            migrationBuilder.DropIndex(
                name: "IX_ProductSideItems_display_order",
                table: "ProductSideItems");

            migrationBuilder.RenameTable(
                name: "ProductSideItems",
                newName: "product_side_items");

            migrationBuilder.RenameIndex(
                name: "IX_ProductSideItems_side_item_product_id",
                table: "product_side_items",
                newName: "IX_product_side_items_side_item_product_id");

            migrationBuilder.RenameIndex(
                name: "IX_ProductSideItems_main_product_id",
                table: "product_side_items",
                newName: "IX_product_side_items_main_product_id");

            migrationBuilder.AddForeignKey(
                name: "FK_product_side_items_Products_main_product_id",
                table: "product_side_items",
                column: "main_product_id",
                principalTable: "Products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_product_side_items_Products_side_item_product_id",
                table: "product_side_items",
                column: "side_item_product_id",
                principalTable: "Products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
