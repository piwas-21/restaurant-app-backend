using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GlobalIngredients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "global_ingredient_id",
                table: "ProductIngredients",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "global_ingredients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    default_name = table.Column<string>(type: "text", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_global_ingredients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "global_ingredient_translations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    global_ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language_code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_global_ingredient_translations", x => x.id);
                    table.ForeignKey(
                        name: "fk_global_ingredient_translations_global_ingredients_global_in~",
                        column: x => x.global_ingredient_id,
                        principalTable: "global_ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_ingredients_global_ingredient_id",
                table: "ProductIngredients",
                column: "global_ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_global_ingredient_translations_global_ingredient_id",
                table: "global_ingredient_translations",
                column: "global_ingredient_id");

            migrationBuilder.AddForeignKey(
                name: "fk_product_ingredients_global_ingredients_global_ingredient_id",
                table: "ProductIngredients",
                column: "global_ingredient_id",
                principalTable: "global_ingredients",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_product_ingredients_global_ingredients_global_ingredient_id",
                table: "ProductIngredients");

            migrationBuilder.DropTable(
                name: "global_ingredient_translations");

            migrationBuilder.DropTable(
                name: "global_ingredients");

            migrationBuilder.DropIndex(
                name: "ix_product_ingredients_global_ingredient_id",
                table: "ProductIngredients");

            migrationBuilder.DropColumn(
                name: "global_ingredient_id",
                table: "ProductIngredients");
        }
    }
}
