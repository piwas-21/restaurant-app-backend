using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders;

/// <summary>
/// Seeds the ONE menu bundle the printer-app E2E suite cannot assert kitchen routing without
/// (issue #238): a <see cref="ProductType.Menu"/> product whose components resolve to DIFFERENT
/// kitchens.
/// </summary>
/// <remarks>
/// <para>
/// That combination is the whole point. A FrontKitchen combo containing BackKitchen fries is the
/// case PR #237 changed the handling of, and the one that failed silently in a live restaurant —
/// the back kitchen got no ticket at all, and the fries printed on the front kitchen's. Neither the
/// backend suite nor the printer-app suite could reproduce it against a real deployment, because
/// staging ships no products at all (`PrinterAPP.E2E/Support/Backend.cs`).
/// </para>
/// <para>
/// <b>Opt-in, and it must stay that way.</b> Unlike every other seeder here this inserts a VISIBLE,
/// ORDERABLE product — a tenant would find "E2E Menu Deal" on their menu and a guest could buy it.
/// It runs only when <c>SeedSettings:SeedE2EMenuFixtures</c> is true, and the default is false
/// precisely so that forgetting to set it is the safe outcome.
/// <para>
/// <b>The invariant that keeps it off production</b> is worth stating exactly, because the obvious
/// one is wrong. It is NOT that the compose file filters the environment: since deploy #65,
/// <c>docker-compose.prod.yml</c> forwards <c>SEED_E2E_MENU_FIXTURES</c> on BOTH boxes, defaulting
/// to <c>false</c>. What pins prod off is that <c>Program.cs</c> adds environment variables LAST in
/// the configuration chain, so that compose default outranks even a <c>SeedSettings</c> block in a
/// box <c>app-secrets.json</c> — which was previously the one way in. The single remaining lever is
/// an explicit <c>SEED_E2E_MENU_FIXTURES=true</c> line in the STAGING box's <c>.env</c>, which is
/// the design intent (see the deploy repo's DEPLOYMENT.md).
/// </para>
/// <para>
/// The value must be literally <c>true</c> or <c>false</c>: anything else fails bool binding, which
/// throws out of <c>MigrateApplicationDatabaseAsync</c>, so the backend never reaches
/// <c>app.Run()</c> and restart-loops.
/// </para>
/// <para>
/// Idempotent by fixed ids: a second boot finds the combo and returns, so it never duplicates and
/// never overwrites an edit someone made on staging.
/// </para>
/// <para>
/// <b>There is no un-seed.</b> Turning the flag off stops future boots from inserting, it does not
/// remove what a previous boot inserted — deliberately, so production's code path stays a pure
/// early return with no delete logic in it at all. To clear a staging tenant, delete the four fixed
/// ids by hand (they are listed below).
/// </para>
/// </remarks>
public static class E2EMenuFixtureSeeder
{
    private const string CreatedBy = "E2ESeed";

    // Fixed ids so the fixture is addressable from the E2E suites and idempotent across boots.
    private static readonly Guid ComboProductId = new("e2e00000-0000-0000-0000-000000000001");
    private static readonly Guid BurgerProductId = new("e2e00000-0000-0000-0000-000000000002");
    private static readonly Guid FriesProductId = new("e2e00000-0000-0000-0000-000000000003");
    private static readonly Guid MenuDefinitionId = new("e2e00000-0000-0000-0000-000000000010");

    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger, SeedSettings settings)
    {
        if (!settings.SeedE2EMenuFixtures)
        {
            return;
        }

        // The sentinel is the MENU DEFINITION, not the product, and that is load-bearing.
        //
        // `Product` is soft-deletable and `DeleteProductCommand` only flips `IsDeleted`, so a
        // product query hidden behind the global filter reports "absent" for a fixture an admin has
        // merely tidied off the staging menu. This seeder would then re-insert the same primary
        // keys, throw a unique violation out of `MigrateApplicationDatabaseAsync`, and stop
        // `app.Run()` from ever being reached — an ordinary cleanup presenting as a crash loop.
        //
        // `MenuDefinition` derives from `Entity`, not `SoftDeleteEntity`: no filter applies to it,
        // its row outlives every delete path the app offers, and its id is one of the ones that
        // would collide. So an ordinary filtered query on it sees exactly what the guard needs to
        // see, with no `IgnoreQueryFilters` anywhere.
        if (await context.Set<MenuDefinition>().AnyAsync(d => d.Id == MenuDefinitionId))
        {
            logger.LogInformation("E2E menu fixture already present — skipping.");
            return;
        }

        var burger = NewProduct(BurgerProductId, "E2E Beef Burger", 12.00m, ProductType.MainItem, KitchenType.FrontKitchen);
        var fries = NewProduct(FriesProductId, "E2E Fries", 4.50m, ProductType.AddOn, KitchenType.BackKitchen);
        var combo = NewProduct(ComboProductId, "E2E Menu Deal", 18.00m, ProductType.Menu, KitchenType.FrontKitchen);

        context.Products.AddRange(burger, fries, combo);

        // Two REQUIRED single-choice sections, each defaulting to its only option, so an automated
        // add needs no selection logic to produce a two-component order.
        var definition = new MenuDefinition
        {
            Id = MenuDefinitionId,
            ProductId = combo.Id,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = CreatedBy,
        };
        definition.Sections.Add(NewSection("Main", 0, burger.Id));
        definition.Sections.Add(NewSection("Side", 1, fries.Id));

        context.Add(definition);
        await context.SaveChangesAsync();

        // WARNING, not information: this just put a visible, orderable product on the menu. If it
        // ever happens somewhere it should not, this is the line someone will grep for.
        logger.LogWarning(
            "Seeded the E2E mixed-kitchen menu fixture (SeedSettings:SeedE2EMenuFixtures=true). "
            + "This inserts VISIBLE, ORDERABLE products and must not be enabled in production.");
    }

    private static MenuSection NewSection(string name, int displayOrder, Guid productId)
    {
        var section = new MenuSection
        {
            Id = Guid.NewGuid(),
            Name = name,
            DisplayOrder = displayOrder,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = CreatedBy,
        };
        section.Items.Add(new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AdditionalPrice = 0m,
            DisplayOrder = 0,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = CreatedBy,
        });
        return section;
    }

    private static Product NewProduct(Guid id, string name, decimal price, ProductType type, KitchenType kitchenType) => new()
    {
        Id = id,
        Name = name,
        Description = "Fixture for the printer-app kitchen-routing E2E (issue #238). Not a real menu item.",
        BasePrice = price,
        IsActive = true,
        IsAvailable = true,
        Type = type,
        KitchenType = kitchenType,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = CreatedBy,
    };
}
