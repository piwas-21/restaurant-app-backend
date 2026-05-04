using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductIngredientsCustomization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "added_ingredients",
                table: "BasketItems",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "customization_price",
                table: "BasketItems",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "excluded_ingredients",
                table: "BasketItems",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "selected_ingredients",
                table: "BasketItems",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductIngredients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_ingredients", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_ingredients_products_product_id",
                        column: x => x.product_id,
                        principalTable: "Products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductIngredientDescriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    product_ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_ingredient_descriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_ingredient_descriptions_product_ingredients_product~",
                        column: x => x.product_ingredient_id,
                        principalTable: "ProductIngredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductIngredientDescriptions_product_ingredient_id_languag~",
                table: "ProductIngredientDescriptions",
                columns: new[] { "product_ingredient_id", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_ingredients_product_id",
                table: "ProductIngredients",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_ProductIngredients_product_id_display_order",
                table: "ProductIngredients",
                columns: new[] { "product_id", "display_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductIngredientDescriptions");

            migrationBuilder.DropTable(
                name: "ProductIngredients");

            migrationBuilder.DropColumn(
                name: "added_ingredients",
                table: "BasketItems");

            migrationBuilder.DropColumn(
                name: "customization_price",
                table: "BasketItems");

            migrationBuilder.DropColumn(
                name: "excluded_ingredients",
                table: "BasketItems");

            migrationBuilder.DropColumn(
                name: "selected_ingredients",
                table: "BasketItems");
        }
    }
}
