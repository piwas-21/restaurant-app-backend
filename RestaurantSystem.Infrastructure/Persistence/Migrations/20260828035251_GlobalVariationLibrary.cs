using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GlobalVariationLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- guards, hand-written; everything below them is scaffolded --------------------
            //
            // This migration tightens two things that have been loose since the tables were created,
            // so it must not assume the existing rows already satisfy them. Both statements are
            // no-ops on data that does, and neither can run away: they are bounded by the same
            // predicate that would otherwise make the ALTER or the CREATE INDEX fail on a live box
            // at deploy time, which is the failure mode they exist to prevent.

            // `product_variation_descriptions` never had the unique (variation, language) index its
            // ingredient twin has had since it was written. Duplicates are unlikely — both write
            // paths replace a variation's whole description set in one SaveChanges, and a payload's
            // Content map cannot repeat a key — but "unlikely" is not a thing to bet a deploy on.
            // The earliest row wins, which is the same row `ProductDtoMapper`'s `g.First()` was
            // most likely already returning.
            migrationBuilder.Sql(@"
                DELETE FROM product_variation_descriptions d
                USING product_variation_descriptions keep
                WHERE d.product_variation_id = keep.product_variation_id
                  AND d.language_code = keep.language_code
                  AND (keep.created_at, keep.id) < (d.created_at, d.id);");

            // `product_variations.name` and `.description` are unbounded `text` today, and
            // `UpdateProductCommandValidator` applied NO variation rules at all until this slice —
            // so a longer value could have been saved by any PUT ever made. The bounds below are
            // already generous (200 / 500 against a validator of 50 / 200); a row that still
            // exceeds them is truncated rather than allowed to fail the ALTER. Not reversible by
            // `Down`, which is why the columns are wide enough that nothing is expected to match.
            migrationBuilder.Sql(@"
                UPDATE product_variations SET name = left(name, 200) WHERE length(name) > 200;
                UPDATE product_variations
                   SET description = left(description, 500)
                 WHERE description IS NOT NULL AND length(description) > 500;");

            migrationBuilder.DropIndex(
                name: "ix_product_variation_descriptions_product_variation_id",
                table: "product_variation_descriptions");

            migrationBuilder.AlterColumn<decimal>(
                name: "price_modifier",
                table: "product_variations",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "product_variations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "product_variations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "global_variation_id",
                table: "product_variations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "global_variations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    default_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    archived_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("pk_global_variations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "global_variation_translations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    global_variation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_global_variation_translations", x => x.id);
                    table.ForeignKey(
                        name: "fk_global_variation_translations_global_variations_global_vari~",
                        column: x => x.global_variation_id,
                        principalTable: "global_variations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_variations_global_variation_id",
                table: "product_variations",
                column: "global_variation_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_variation_descriptions_product_variation_id_languag~",
                table: "product_variation_descriptions",
                columns: new[] { "product_variation_id", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_global_variation_translations_global_variation_id_language_~",
                table: "global_variation_translations",
                columns: new[] { "global_variation_id", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_global_variations_default_name",
                table: "global_variations",
                column: "default_name");

            migrationBuilder.AddForeignKey(
                name: "fk_product_variations_global_variations_global_variation_id",
                table: "product_variations",
                column: "global_variation_id",
                principalTable: "global_variations",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_product_variations_global_variations_global_variation_id",
                table: "product_variations");

            migrationBuilder.DropTable(
                name: "global_variation_translations");

            migrationBuilder.DropTable(
                name: "global_variations");

            migrationBuilder.DropIndex(
                name: "ix_product_variations_global_variation_id",
                table: "product_variations");

            migrationBuilder.DropIndex(
                name: "IX_product_variation_descriptions_product_variation_id_languag~",
                table: "product_variation_descriptions");

            migrationBuilder.DropColumn(
                name: "global_variation_id",
                table: "product_variations");

            migrationBuilder.AlterColumn<decimal>(
                name: "price_modifier",
                table: "product_variations",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "product_variations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "product_variations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_variation_descriptions_product_variation_id",
                table: "product_variation_descriptions",
                column: "product_variation_id");
        }
    }
}
