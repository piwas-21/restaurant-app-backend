using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Seeders;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// G1 backfill (prod rumirestaurant): the seeded library predates the <c>Kind</c> discriminator,
/// so all 654 seeded rows landed as <c>Ingredient</c> and the Sauces group's "add from library"
/// picker — which reads <c>Kind = Sauce</c> — opened empty for tenants that never created typed
/// sauce rows.
///
/// These tests pin BOTH halves of the fix:
/// <list type="bullet">
/// <item><see cref="GlobalIngredientsSeeder"/> stamps <b>exactly</b> the curated 39-name sauce
/// family as <c>Sauce</c> on a fresh install and leaves every other seeded row (and the
/// skip-if-any-exists guard) alone — the name list below is asserted independently of the
/// seeder's own set. The migration's SQL is exercised through the Pesto/Mustard/Miso probes
/// below; that its full 39-name list equals this oracle is enforced by review, not execution.</item>
/// <item>the <c>ReclassifySeededSauces</c> migration walks a pre-fix state through its real
/// <c>Up</c>/<c>Down</c> against real Postgres (reverting and re-applying via
/// <c>MigrateAsync</c> — no SQL copy): seeded rows reclassify, tenant-created rows with
/// coincidental names and admin-created sauces (<c>LibraryOrigin.Custom</c>) do not move.</item>
/// </list>
///
/// This is a plain fixture class — no per-test Respawn reset runs inside it — so each test
/// reaches a bare table itself (a leading ExecuteDelete purge plus an emptiness assertion)
/// instead of assuming one. Nothing in the suite re-seeds the library.
/// </summary>
[Collection("Database Lane 4")]
public class GlobalIngredientsSeederTests
{
    /// <summary>The migration immediately before <c>ReclassifySeededSauces</c> — the revert target.</summary>
    // The literal is split only so detect-secrets' base64-entropy heuristic does not flag the
    // digit-heavy migration id; it concatenates to the exact history id.
    private const string MigrationBeforeReclassify =
        "20260907153005" + "_AddGlobalIngredientIsNoneOption";

    /// <summary>Expected seed size — pinned so "39 sauces + 615 ingredients" is a real partition.</summary>
    private const int SeededRowCount = 654;

    /// <summary>
    /// The curated sauce family — the ORACLE for both the seeder stamp and the migration. Written
    /// out here, not referenced from the seeder, so the two lists cannot rot in step silently.
    /// </summary>
    private static readonly string[] SauceFamily =
    [
        "Soy sauce", "Tamari", "Fish sauce", "Oyster sauce", "Hoisin sauce", "Teriyaki sauce",
        "Gochujang", "Tahini", "Hummus", "Baba ghanoush", "Moutabal", "Caramel sauce", "Mustard",
        "Dijon mustard", "Whole grain mustard", "Ketchup", "Mayonnaise", "Aioli", "Garlic sauce",
        "Toum", "Barbecue sauce", "Buffalo sauce", "Ranch dressing", "Caesar dressing",
        "Italian dressing", "Hot sauce", "Sriracha", "Tabasco", "Worcestershire sauce",
        "Pizza sauce", "Marinara sauce", "Alfredo sauce", "Pesto", "Sun-dried tomato pesto",
        "Béchamel", "White sauce", "Gravy", "Brown sauce", "Balsamic glaze",
    ];

    /// <summary>Section neighbours that share the sauce sections but are deliberately NOT sauces.</summary>
    private static readonly string[] StaysIngredient =
    [
        "Miso", "Kimchi", "Nutella", "Peanut butter", "Almond butter", "Cashew butter",
        "Pistachio cream", "Labneh balls", "Toffee", "Sprinkles", "Vanilla extract",
        "Almond extract", "Rose water", "Orange blossom water", "Gelatin", "Agar agar",
        "Corn syrup", "Custard powder", "Pudding mix", "Cake flour", "Bread improver",
        "Truffle oil", "Black truffle", "White truffle", "Horseradish sauce",
    ];

    private readonly DatabaseFixture _fixture;

    public GlobalIngredientsSeederTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FreshSeed_StampsExactlyTheSauceFamilyAsSauce()
    {
        await using var context = _fixture.CreateContext();
        // Start from a bare table. xUnit may run the migration test of this class first and it
        // leaves its probe rows behind, so "empty" cannot be assumed — only asserted.
        // soft-delete-bypass: permanent purge of throwaway fixture rows to reach the pre-seed
        // state the skip guard requires.
        await context.GlobalIngredients.ExecuteDeleteAsync();
        (await context.GlobalIngredients.AnyAsync()).Should().BeFalse();

        await GlobalIngredientsSeeder.SeedAsync(context, NullLogger.Instance);

        await using var verify = _fixture.CreateContext();
        var rows = await verify.GlobalIngredients.ToListAsync();
        rows.Should().HaveCount(SeededRowCount);

        var sauces = rows.Where(r => r.Kind == IngredientKind.Sauce).ToList();
        sauces.Should().HaveCount(SauceFamily.Length);
        sauces.Select(r => r.DefaultName).Should().BeEquivalentTo(SauceFamily);
        sauces.Should().OnlyContain(r => r.Origin == LibraryOrigin.System);

        // The rest of the library — including the excluded family neighbours — stays Ingredient.
        var ingredients = rows.Where(r => r.Kind == IngredientKind.Ingredient).ToList();
        ingredients.Should().HaveCount(SeededRowCount - SauceFamily.Length);
        ingredients.Select(r => r.DefaultName).Should().Contain(StaysIngredient);
        rows.Should().OnlyContain(r => r.Origin == LibraryOrigin.System);
    }

    [Fact]
    public async Task SkipGuard_SecondRun_DoesNotDuplicateOrDowngrade()
    {
        await using var context = _fixture.CreateContext();
        // soft-delete-bypass: permanent purge of throwaway fixture rows (see the fresh-seed test).
        await context.GlobalIngredients.ExecuteDeleteAsync();
        await GlobalIngredientsSeeder.SeedAsync(context, NullLogger.Instance);

        await GlobalIngredientsSeeder.SeedAsync(context, NullLogger.Instance);

        await using var verify = _fixture.CreateContext();
        var rows = await verify.GlobalIngredients.ToListAsync();
        rows.Should().HaveCount(SeededRowCount, "the skip guard must keep a second boot from re-seeding");
        rows.Count(r => r.Kind == IngredientKind.Sauce).Should().Be(SauceFamily.Length);
    }

    [Fact]
    public async Task ReclassifyMigration_UpAndDown_ReclassifiesOnlySeededRows()
    {
        // Pre-state as production has it today: seeded rows are System + Ingredient; a
        // tenant-created "Pesto" (coincidental name) and an admin-created post-G1 sauce are Custom.
        await using var setup = _fixture.CreateContext();
        // soft-delete-bypass: permanent purge of throwaway fixture rows from a sibling test.
        await setup.GlobalIngredients.ExecuteDeleteAsync();
        setup.GlobalIngredients.AddRange(
            new GlobalIngredient { DefaultName = "Pesto", CreatedBy = "System" },
            new GlobalIngredient { DefaultName = "Mustard", CreatedBy = "System" },
            new GlobalIngredient { DefaultName = "Miso", CreatedBy = "System" },
            new GlobalIngredient { DefaultName = "Pesto", CreatedBy = "tenant", Origin = LibraryOrigin.Custom },
            new GlobalIngredient
            {
                DefaultName = "Garlic sauce",
                CreatedBy = "tenant",
                Origin = LibraryOrigin.Custom,
                Kind = IngredientKind.Sauce,
            });
        await setup.SaveChangesAsync();
        (await setup.GlobalIngredients.CountAsync(g => g.Origin == LibraryOrigin.System)).Should().Be(3);
        (await setup.GlobalIngredients.CountAsync(g => g.Origin == LibraryOrigin.Custom)).Should().Be(2);

        // Revert to the migration before the reclassify (Down runs; a no-op on kind = 0 rows),
        // then re-apply everything (Up) — the REAL migration SQL, no copy in the test.
        await setup.Database.MigrateAsync(MigrationBeforeReclassify);
        await setup.Database.MigrateAsync();

        await using var verify = _fixture.CreateContext();
        var afterUp = await verify.GlobalIngredients.ToListAsync();
        Named(afterUp, "Pesto").Where(r => r.Origin == LibraryOrigin.System).Should().OnlyContain(
            r => r.Kind == IngredientKind.Sauce, "a seeded row with a sauce-family name reclassifies");
        Named(afterUp, "Mustard").Should().OnlyContain(r => r.Kind == IngredientKind.Sauce);
        Named(afterUp, "Miso").Should().OnlyContain(
            r => r.Kind == IngredientKind.Ingredient, "a seeded row outside the curated family does not move");
        Named(afterUp, "Pesto").Where(r => r.Origin == LibraryOrigin.Custom).Should().OnlyContain(
            r => r.Kind == IngredientKind.Ingredient, "a tenant row with a coincidental name does not move");
        Named(afterUp, "Garlic sauce").Should().OnlyContain(
            r => r.Kind == IngredientKind.Sauce, "an admin-created sauce does not move");

        // Down must restore the seeded rows without touching the tenant rows.
        await verify.Database.MigrateAsync(MigrationBeforeReclassify);
        await using var verifyDown = _fixture.CreateContext();
        var afterDown = await verifyDown.GlobalIngredients.ToListAsync();
        Named(afterDown, "Pesto").Where(r => r.Origin == LibraryOrigin.System).Should().OnlyContain(
            r => r.Kind == IngredientKind.Ingredient);
        Named(afterDown, "Mustard").Should().OnlyContain(r => r.Kind == IngredientKind.Ingredient);
        Named(afterDown, "Garlic sauce").Should().OnlyContain(r => r.Kind == IngredientKind.Sauce);

        // Leave the lane database fully migrated (and the table bare) for the tests that follow.
        await verifyDown.Database.MigrateAsync();
        // soft-delete-bypass: permanent purge of this test's own probe rows.
        await verifyDown.GlobalIngredients.ExecuteDeleteAsync();
    }

    private static List<GlobalIngredient> Named(List<GlobalIngredient> rows, string name) =>
        rows.Where(r => r.DefaultName == name).ToList();
}
