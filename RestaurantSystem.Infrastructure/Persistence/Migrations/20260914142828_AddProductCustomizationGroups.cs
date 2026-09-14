using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddProductCustomizationGroups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("product_customization_groups", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            product_id = table.Column<Guid>(type: "uuid", nullable: false),
            name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            display_order = table.Column<int>(type: "integer", nullable: false),
            is_required = table.Column<bool>(type: "boolean", nullable: false),
            min_selection = table.Column<int>(type: "integer", nullable: false),
            max_selection = table.Column<int>(type: "integer", nullable: false),
            included_free_units = table.Column<int>(type: "integer", nullable: false),
            is_active = table.Column<bool>(type: "boolean", nullable: false),
            created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
            updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            created_by = table.Column<string>(type: "text", nullable: false),
            updated_by = table.Column<string>(type: "text", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("pk_product_customization_groups", x => x.id);
            table.CheckConstraint("ck_product_customization_group_free", "included_free_units >= 0");
            table.CheckConstraint("ck_product_customization_group_max", "max_selection >= min_selection");
            table.CheckConstraint("ck_product_customization_group_min", "min_selection >= 0");
            table.ForeignKey("fk_product_customization_groups_products_product_id", x => x.product_id,
                "Products", "id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("product_customization_group_descriptions", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            product_customization_group_id = table.Column<Guid>(type: "uuid", nullable: false),
            language_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
            name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
            updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            created_by = table.Column<string>(type: "text", nullable: false),
            updated_by = table.Column<string>(type: "text", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("pk_product_customization_group_descriptions", x => x.id);
            table.ForeignKey("fk_product_customization_group_descriptions_product_customizat~",
                x => x.product_customization_group_id, "product_customization_groups", "id",
                onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("product_customization_ingredient_options", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            product_customization_group_id = table.Column<Guid>(type: "uuid", nullable: false),
            product_ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
            display_order = table.Column<int>(type: "integer", nullable: false),
            is_default = table.Column<bool>(type: "boolean", nullable: false),
            created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
            updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            created_by = table.Column<string>(type: "text", nullable: false),
            updated_by = table.Column<string>(type: "text", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("pk_product_customization_ingredient_options", x => x.id);
            table.ForeignKey("fk_product_customization_ingredient_options_product_customizat~",
                x => x.product_customization_group_id, "product_customization_groups", "id",
                onDelete: ReferentialAction.Cascade);
            table.ForeignKey("fk_product_customization_ingredient_options_productingredients~",
                x => x.product_ingredient_id, "ProductIngredients", "id",
                onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("product_customization_product_options", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            product_customization_group_id = table.Column<Guid>(type: "uuid", nullable: false),
            option_product_id = table.Column<Guid>(type: "uuid", nullable: false),
            additional_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
            display_order = table.Column<int>(type: "integer", nullable: false),
            is_default = table.Column<bool>(type: "boolean", nullable: false),
            created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
            updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            created_by = table.Column<string>(type: "text", nullable: false),
            updated_by = table.Column<string>(type: "text", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("pk_product_customization_product_options", x => x.id);
            table.CheckConstraint("ck_product_customization_product_option_price", "additional_price >= 0");
            table.ForeignKey("fk_product_customization_product_options_product_customization~",
                x => x.product_customization_group_id, "product_customization_groups", "id",
                onDelete: ReferentialAction.Cascade);
            table.ForeignKey("fk_product_customization_product_options_products_option_produ~",
                x => x.option_product_id, "Products", "id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateIndex("IX_product_customization_group_descriptions_product_customizat~",
            "product_customization_group_descriptions", new[] { "product_customization_group_id", "language_code" }, unique: true);
        migrationBuilder.CreateIndex("IX_product_customization_groups_product_id_display_order",
            "product_customization_groups", new[] { "product_id", "display_order" });
        migrationBuilder.CreateIndex("IX_product_customization_ingredient_options_product_customizat~",
            "product_customization_ingredient_options", new[] { "product_customization_group_id", "display_order" });
        migrationBuilder.CreateIndex("ix_product_customization_ingredient_options_product_ingredient~",
            "product_customization_ingredient_options", "product_ingredient_id", unique: true);
        migrationBuilder.CreateIndex("ix_product_customization_product_options_option_product_id",
            "product_customization_product_options", "option_product_id");
        migrationBuilder.CreateIndex("IX_product_customization_product_options_product_customizatio~1",
            "product_customization_product_options", new[] { "product_customization_group_id", "option_product_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_product_customization_product_options_product_customization~",
            "product_customization_product_options", new[] { "product_customization_group_id", "display_order" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "product_customization_group_descriptions");
        migrationBuilder.DropTable(name: "product_customization_ingredient_options");
        migrationBuilder.DropTable(name: "product_customization_product_options");
        migrationBuilder.DropTable(name: "product_customization_groups");
    }
}
