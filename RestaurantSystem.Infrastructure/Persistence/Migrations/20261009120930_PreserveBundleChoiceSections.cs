using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveBundleChoiceSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "section_id",
                table: "OrderItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "section_id",
                table: "BasketItems",
                type: "uuid",
                nullable: true);

            // Annotate historical rows only when stable identities identify exactly one choice
            // section. Prices, names, quantities and order/line identities are untouched.
            migrationBuilder.Sql("""
                WITH membership AS (
                    SELECT child.id, (array_agg(DISTINCT section.id))[1] AS section_id
                    FROM "OrderItems" child
                    JOIN "OrderItems" parent ON parent.id = child.parent_order_item_id
                    JOIN menu_definitions definition ON definition.product_id = parent.product_id
                    JOIN menu_sections section ON section.menu_definition_id = definition.id
                    JOIN menu_section_items option ON option.menu_section_id = section.id
                        AND option.product_id = child.product_id
                        AND option.product_variation_id IS NOT DISTINCT FROM child.product_variation_id
                    WHERE child.section_id IS NULL AND child.kind = 0
                    GROUP BY child.id
                    HAVING count(DISTINCT section.id) = 1
                )
                UPDATE "OrderItems" child SET section_id = membership.section_id
                FROM membership WHERE child.id = membership.id;
                """);
            // Annotate historical rows only when stable identities identify exactly one choice
            // section. Prices, names, quantities and order/line identities are untouched.
            migrationBuilder.Sql("""
                WITH membership AS (
                    SELECT child.id, (array_agg(DISTINCT section.id))[1] AS section_id
                    FROM "BasketItems" child
                    JOIN "BasketItems" parent ON parent.id = child.parent_basket_item_id
                    JOIN menu_definitions definition ON definition.product_id = parent.product_id
                    JOIN menu_sections section ON section.menu_definition_id = definition.id
                    JOIN menu_section_items option ON option.menu_section_id = section.id
                        AND option.product_id = child.product_id
                        AND option.product_variation_id IS NOT DISTINCT FROM child.product_variation_id
                    WHERE child.section_id IS NULL AND child.product_customization_option_id IS NULL
                    GROUP BY child.id
                    HAVING count(DISTINCT section.id) = 1
                )
                UPDATE "BasketItems" child SET section_id = membership.section_id
                FROM membership WHERE child.id = membership.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "section_id",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "section_id",
                table: "BasketItems");
        }
    }
}
