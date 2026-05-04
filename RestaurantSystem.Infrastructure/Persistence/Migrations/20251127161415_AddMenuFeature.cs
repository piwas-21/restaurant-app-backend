using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_order_item_id",
                table: "OrderItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "menu_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_always_available = table.Column<bool>(type: "boolean", nullable: false),
                    start_time = table.Column<TimeSpan>(type: "time", nullable: true),
                    end_time = table.Column<TimeSpan>(type: "time", nullable: true),
                    available_monday = table.Column<bool>(type: "boolean", nullable: false),
                    available_tuesday = table.Column<bool>(type: "boolean", nullable: false),
                    available_wednesday = table.Column<bool>(type: "boolean", nullable: false),
                    available_thursday = table.Column<bool>(type: "boolean", nullable: false),
                    available_friday = table.Column<bool>(type: "boolean", nullable: false),
                    available_saturday = table.Column<bool>(type: "boolean", nullable: false),
                    available_sunday = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_definitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_menu_definitions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "Products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_sections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    menu_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    min_selection = table.Column<int>(type: "integer", nullable: false),
                    max_selection = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_sections", x => x.id);
                    table.ForeignKey(
                        name: "fk_menu_sections_menu_definitions_menu_definition_id",
                        column: x => x.menu_definition_id,
                        principalTable: "menu_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_section_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    menu_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    additional_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_section_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_menu_section_items_menu_sections_menu_section_id",
                        column: x => x.menu_section_id,
                        principalTable: "menu_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_menu_section_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "Products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_items_parent_order_item_id",
                table: "OrderItems",
                column: "parent_order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_definitions_product_id",
                table: "menu_definitions",
                column: "product_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_menu_section_items_menu_section_id",
                table: "menu_section_items",
                column: "menu_section_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_section_items_product_id",
                table: "menu_section_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_sections_menu_definition_id",
                table: "menu_sections",
                column: "menu_definition_id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_order_items_parent_order_item_id",
                table: "OrderItems",
                column: "parent_order_item_id",
                principalTable: "OrderItems",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_order_items_order_items_parent_order_item_id",
                table: "OrderItems");

            migrationBuilder.DropTable(
                name: "menu_section_items");

            migrationBuilder.DropTable(
                name: "menu_sections");

            migrationBuilder.DropTable(
                name: "menu_definitions");

            migrationBuilder.DropIndex(
                name: "ix_order_items_parent_order_item_id",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "parent_order_item_id",
                table: "OrderItems");
        }
    }
}
