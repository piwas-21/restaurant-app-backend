using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMenuFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_basket_item_id",
                table: "BasketItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_basket_items_parent_basket_item_id",
                table: "BasketItems",
                column: "parent_basket_item_id");

            migrationBuilder.AddForeignKey(
                name: "fk_basket_items_basket_items_parent_basket_item_id",
                table: "BasketItems",
                column: "parent_basket_item_id",
                principalTable: "BasketItems",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_basket_items_basket_items_parent_basket_item_id",
                table: "BasketItems");

            migrationBuilder.DropIndex(
                name: "ix_basket_items_parent_basket_item_id",
                table: "BasketItems");

            migrationBuilder.DropColumn(
                name: "parent_basket_item_id",
                table: "BasketItems");
        }
    }
}
