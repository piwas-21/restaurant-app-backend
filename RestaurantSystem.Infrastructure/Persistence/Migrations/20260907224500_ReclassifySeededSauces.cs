using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{

    /// <summary>
    /// Reclassifies the seeded library's sauce family from <c>Ingredient</c> to <c>Sauce</c>
    /// (G1 backfill, prod rumirestaurant).
    ///
    /// The global ingredient library was seeded before the <c>Kind</c> discriminator existed, so
    /// all 654 seeded rows carry <c>kind = 0</c> — including the 39 sauce-family rows named here.
    /// The Sauces group's "add from library" picker reads <c>Kind = Sauce</c>
    /// (<c>SauceSelectionRule</c>), so a tenant that never created typed sauce rows sees an EMPTY
    /// picker. Fresh installs are fixed by the seeder; every database seeded before that fix is
    /// repaired by this migration.
    ///
    /// <b>Data-only and idempotent.</b> A row moves only when <c>origin = 0</c>
    /// (<c>LibraryOrigin.System</c>, i.e. platform-seeded) AND <c>kind = 0</c>
    /// (<c>IngredientKind.Ingredient</c>). Tenant rows that happen to share a name — including
    /// admin-created sauces from after G1, which are <c>LibraryOrigin.Custom</c> — are untouched,
    /// and a re-run changes nothing. Soft-deleted seeded rows move too, so a restore comes back
    /// as the sauce it is.
    ///
    /// <b>This is a curated subset.</b> Section neighbours like Miso, Kimchi, Nutella, the nut
    /// butters, the extracts, the truffles and the baking aids are deliberately NOT sauces.
    ///
    /// The names must stay in step with <c>SeededSauceFamily.DefaultNames</c>,
    /// which stamps the same list on a fresh install. <b>No model change</b> — the snapshot is
    /// intentionally untouched.
    /// </summary>
    public partial class ReclassifySeededSauces : Migration
    {
        /// <summary>The seeded rows to reclassify, by <c>default_name</c>, verbatim from the seeder.</summary>
        private const string SeededSauceNameList =
            """
            'Soy sauce',
            'Tamari',
            'Fish sauce',
            'Oyster sauce',
            'Hoisin sauce',
            'Teriyaki sauce',
            'Gochujang',
            'Tahini',
            'Hummus',
            'Baba ghanoush',
            'Moutabal',
            'Caramel sauce',
            'Mustard',
            'Dijon mustard',
            'Whole grain mustard',
            'Ketchup',
            'Mayonnaise',
            'Aioli',
            'Garlic sauce',
            'Toum',
            'Barbecue sauce',
            'Buffalo sauce',
            'Ranch dressing',
            'Caesar dressing',
            'Italian dressing',
            'Hot sauce',
            'Sriracha',
            'Tabasco',
            'Worcestershire sauce',
            'Pizza sauce',
            'Marinara sauce',
            'Alfredo sauce',
            'Pesto',
            'Sun-dried tomato pesto',
            'Béchamel',
            'White sauce',
            'Gravy',
            'Brown sauce',
            'Balsamic glaze'
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE global_ingredients
                SET kind = 1        -- IngredientKind.Sauce
                WHERE origin = 0    -- LibraryOrigin.System
                  AND kind = 0      -- IngredientKind.Ingredient
                  AND default_name IN (
                      {SeededSauceNameList}
                  );
                """);
        }

        /// <summary>
        /// Exact inverse: demote the same seeded rows back to <c>Ingredient</c>. After this
        /// migration (or the corrected seeder) the only System rows carrying these names with
        /// <c>kind = 1</c> are ones this family stamped, so demotion restores the pre-migration
        /// state; tenant-owned rows (<c>origin = 1</c>) are never touched in either direction.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE global_ingredients
                SET kind = 0        -- IngredientKind.Ingredient
                WHERE origin = 0    -- LibraryOrigin.System
                  AND kind = 1      -- IngredientKind.Sauce
                  AND default_name IN (
                      {SeededSauceNameList}
                  );
                """);
        }
    }
}
