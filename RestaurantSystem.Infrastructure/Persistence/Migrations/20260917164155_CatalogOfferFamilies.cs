using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogOfferFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bundle_presentation_mode",
                table: "RestaurantInfo",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "LegacySeparate");

            migrationBuilder.AddColumn<Guid>(
                name: "product_variation_id",
                table: "menu_section_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_offer_product_id",
                table: "menu_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_offer_variation_id",
                table: "menu_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_menu_section_items_product_variation_id",
                table: "menu_section_items",
                column: "product_variation_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_definitions_parent_offer_variation_id",
                table: "menu_definitions",
                column: "parent_offer_variation_id");

            migrationBuilder.CreateIndex(
                name: "ux_menu_definitions_parent_offer_product_id",
                table: "menu_definitions",
                column: "parent_offer_product_id",
                unique: true,
                filter: "\"parent_offer_product_id\" IS NOT NULL AND \"parent_offer_variation_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_menu_definitions_parent_offer_variation",
                table: "menu_definitions",
                columns: new[] { "parent_offer_product_id", "parent_offer_variation_id" },
                unique: true,
                filter: "\"parent_offer_product_id\" IS NOT NULL AND \"parent_offer_variation_id\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_menu_definitions_products_parent_offer_product_id",
                table: "menu_definitions",
                column: "parent_offer_product_id",
                principalTable: "Products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_definitions_productvariations_parent_offer_variation_id",
                table: "menu_definitions",
                column: "parent_offer_variation_id",
                principalTable: "product_variations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_section_items_productvariations_product_variation_id",
                table: "menu_section_items",
                column: "product_variation_id",
                principalTable: "product_variations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_menu_definitions_products_parent_offer_product_id",
                table: "menu_definitions");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_definitions_productvariations_parent_offer_variation_id",
                table: "menu_definitions");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_section_items_productvariations_product_variation_id",
                table: "menu_section_items");

            migrationBuilder.DropIndex(
                name: "ix_menu_section_items_product_variation_id",
                table: "menu_section_items");

            migrationBuilder.DropIndex(
                name: "ix_menu_definitions_parent_offer_variation_id",
                table: "menu_definitions");

            migrationBuilder.DropIndex(
                name: "ux_menu_definitions_parent_offer_product_id",
                table: "menu_definitions");

            migrationBuilder.DropIndex(
                name: "ux_menu_definitions_parent_offer_variation",
                table: "menu_definitions");

            migrationBuilder.DropColumn(
                name: "bundle_presentation_mode",
                table: "RestaurantInfo");

            migrationBuilder.DropColumn(
                name: "product_variation_id",
                table: "menu_section_items");

            migrationBuilder.DropColumn(
                name: "parent_offer_product_id",
                table: "menu_definitions");

            migrationBuilder.DropColumn(
                name: "parent_offer_variation_id",
                table: "menu_definitions");
        }
    }
}
