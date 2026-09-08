using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders;

/// <summary>
/// The library's curated SAUCE family, by exact <see cref="GlobalIngredient.DefaultName"/> (G1
/// backfill, prod rumirestaurant). The library was seeded before the <c>Kind</c> discriminator
/// existed, so every row landed as <see cref="IngredientKind.Ingredient"/> and the Sauces group's
/// "add from library" picker (SauceSelectionRule reads <c>Kind = Sauce</c>) opened empty on
/// installs that never created typed sauce rows.
///
/// <para>
/// Deliberately NOT the whole sauce-adjacent surface: spreads and nut butters (Nutella, the
/// butters, Pistachio cream, Labneh balls), the extracts and flavoured waters, the truffle family,
/// and the baking aids (Gelatin, Agar agar, Corn syrup, Custard powder, Pudding mix, Cake flour,
/// Bread improver) stay ingredients — including the sauce-named Horseradish sauce.
/// </para>
///
/// <para>
/// <see cref="GlobalIngredientsSeeder"/> stamps these names on a fresh install;
/// <c>migrations/20260907224500_ReclassifySeededSauces</c> recomputes the same list for databases
/// seeded before this existed — keep the two lists in step. The test oracle
/// (<c>GlobalIngredientsSeederTests.SauceFamily</c>) pins both against a written-out copy of the
/// family, so a name drifting anywhere fails the suite.
/// </para>
///
/// <para>
/// Its own file, not a field of the seeder: each name must appear verbatim exactly once here,
/// while the seeder's translation sections already repeat most of them — SonarCloud's
/// string-literal rule (S1192) counts occurrences per file, and a second copy inside the seeder
/// turns every one of these lines into a NEW code smell that fails the new-code quality gate.
/// </para>
/// </summary>
internal static class SeededSauceFamily
{
    public static readonly HashSet<string> DefaultNames = new()
    {
        "Soy sauce", "Tamari", "Fish sauce", "Oyster sauce", "Hoisin sauce", "Teriyaki sauce",
        "Gochujang", "Tahini", "Hummus", "Baba ghanoush", "Moutabal", "Caramel sauce", "Mustard",
        "Dijon mustard", "Whole grain mustard", "Ketchup", "Mayonnaise", "Aioli", "Garlic sauce",
        "Toum", "Barbecue sauce", "Buffalo sauce", "Ranch dressing", "Caesar dressing",
        "Italian dressing", "Hot sauce", "Sriracha", "Tabasco", "Worcestershire sauce",
        "Pizza sauce", "Marinara sauce", "Alfredo sauce", "Pesto", "Sun-dried tomato pesto",
        "Béchamel", "White sauce", "Gravy", "Brown sauce", "Balsamic glaze"
    };
}
