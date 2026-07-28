using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniquePrimaryProductCategoryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // REPAIR BEFORE CONSTRAINING. The index below is unique, so a single product that
            // already carries two primary categories would fail this migration — and a failed
            // migration stops `app.Run()` from ever being reached, i.e. it takes the API down on
            // deploy rather than surfacing as a warning. Nothing has ever prevented that state
            // (§9.5: there was no configuration for this entity at all), so it cannot be assumed
            // absent on a tenant's database just because it is absent locally.
            //
            // The demotion is DETERMINISTIC — lowest display_order, ties broken by id. That is a
            // CHOICE, not a reconstruction of prior behaviour: there is no ordered read to
            // reconstruct (no include path orders ProductCategories, which is the whole reason
            // `FirstOrDefault(pc => pc.IsPrimary)` was nondeterministic). In a state this corrupt any
            // winner is arbitrary, so it picks the one an admin would see first. No-op on clean data.
            // Reported, not silent: this CHANGES the effective availability of every affected
            // product (that is the point — the restriction was nondeterministic before), and on a
            // tenant database nobody would otherwise know it happened or to which items. The count
            // lands in the deploy log next to the migration that caused it.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE demoted integer;
                BEGIN
                UPDATE product_categories pc
                SET is_primary = false
                WHERE pc.is_primary = true
                  AND pc.id <> (
                      SELECT keep.id
                      FROM product_categories keep
                      WHERE keep.product_id = pc.product_id
                        AND keep.is_primary = true
                      ORDER BY keep.display_order, keep.id
                      LIMIT 1
                  );
                GET DIAGNOSTICS demoted = ROW_COUNT;
                IF demoted > 0 THEN
                    RAISE NOTICE '§9.5: demoted % duplicate primary product-category row(s). Affected products had a nondeterministic channel restriction until now.', demoted;
                END IF;
                END $$;");

            migrationBuilder.CreateIndex(
                name: "ix_product_categories_product_id_is_primary_unique",
                table: "product_categories",
                columns: new[] { "product_id", "is_primary" },
                unique: true,
                filter: "\"is_primary\" = true");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Drops the constraint only. <b>The repair does not roll back</b> — nothing records which
        /// rows were demoted, so rolling this migration back leaves the data repaired and merely
        /// permits duplicates again. That is acceptable for a data-repair migration, but an operator
        /// rolling back should know the data does not come with them.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_product_categories_product_id_is_primary_unique",
                table: "product_categories");
        }
    }
}
