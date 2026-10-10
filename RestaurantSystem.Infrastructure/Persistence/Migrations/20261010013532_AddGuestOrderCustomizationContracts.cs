using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestOrderCustomizationContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "customer_step_manifest_json",
                table: "Products",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "customer_step_manifest_revision",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "composition_role",
                table: "OrderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "configuration_scope",
                table: "OrderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "menu_section_item_id",
                table: "OrderItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_component_order_item_id",
                table: "OrderItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "presentation_label",
                table: "OrderItems",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "presentation_order",
                table: "OrderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quantity_basis",
                table: "OrderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "suggested_side_item_id",
                table: "OrderItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "composition_role",
                table: "OrderItemIngredients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "configuration_scope",
                table: "OrderItemIngredients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "presentation_order",
                table: "OrderItemIngredients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quantity_basis",
                table: "OrderItemIngredients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "composition_role",
                table: "BasketItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "configuration_scope",
                table: "BasketItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "menu_section_item_id",
                table: "BasketItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_component_menu_section_item_id",
                table: "BasketItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "presentation_label",
                table: "BasketItems",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "presentation_order",
                table: "BasketItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quantity_basis",
                table: "BasketItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_parent_component_order_item_id",
                table: "OrderItems",
                column: "parent_component_order_item_id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_OrderItems_parent_component_order_item_id",
                table: "OrderItems",
                column: "parent_component_order_item_id",
                principalTable: "OrderItems",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_OrderItems_parent_component_order_item_id",
                table: "OrderItems");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_parent_component_order_item_id",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "customer_step_manifest_json",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "customer_step_manifest_revision",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "composition_role",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "configuration_scope",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "menu_section_item_id",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "parent_component_order_item_id",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "presentation_label",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "presentation_order",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "quantity_basis",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "suggested_side_item_id",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "composition_role",
                table: "OrderItemIngredients");

            migrationBuilder.DropColumn(
                name: "configuration_scope",
                table: "OrderItemIngredients");

            migrationBuilder.DropColumn(
                name: "presentation_order",
                table: "OrderItemIngredients");

            migrationBuilder.DropColumn(
                name: "quantity_basis",
                table: "OrderItemIngredients");

            migrationBuilder.DropColumn(
                name: "composition_role",
                table: "BasketItems");

            migrationBuilder.DropColumn(
                name: "configuration_scope",
                table: "BasketItems");

            migrationBuilder.DropColumn(
                name: "menu_section_item_id",
                table: "BasketItems");

            migrationBuilder.DropColumn(
                name: "parent_component_menu_section_item_id",
                table: "BasketItems");

            migrationBuilder.DropColumn(
                name: "presentation_label",
                table: "BasketItems");

            migrationBuilder.DropColumn(
                name: "presentation_order",
                table: "BasketItems");

            migrationBuilder.DropColumn(
                name: "quantity_basis",
                table: "BasketItems");
        }
    }
}
