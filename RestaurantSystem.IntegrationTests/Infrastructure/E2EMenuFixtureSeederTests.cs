using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Infrastructure.Persistence.Seeders;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// The E2E menu fixture (issue #238) is the only seeder that inserts a VISIBLE, ORDERABLE product,
/// so the property worth testing hardest is the one that keeps it off a real menu: it does nothing
/// unless explicitly enabled. A regression here does not fail — it quietly puts "E2E Menu Deal" in
/// front of a paying guest.
/// </summary>
[Collection("Database Lane 1")]
public class E2EMenuFixtureSeederTests : IntegrationTestBase
{
    public E2EMenuFixtureSeederTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const string ComboName = "E2E Menu Deal";

    [Fact]
    public async Task Disabled_SeedsNothing_SoAProductionBootStaysClean()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await E2EMenuFixtureSeeder.SeedAsync(context, NullLogger.Instance, new SeedSettings { SeedE2EMenuFixtures = false });

        (await context.Products.AnyAsync(p => p.Name == ComboName)).Should().BeFalse(
            "the fixture must never reach a tenant's menu by default");
    }

    [Fact]
    public async Task Enabled_SeedsAComboWhoseComponentsSitInDIFFERENTKitchens()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await E2EMenuFixtureSeeder.SeedAsync(context, NullLogger.Instance, new SeedSettings { SeedE2EMenuFixtures = true });

        var combo = await context.Products
            .Include(p => p.MenuDefinition!)
                .ThenInclude(d => d.Sections)
                    .ThenInclude(s => s.Items)
                        .ThenInclude(i => i.Product)
            .SingleAsync(p => p.Name == ComboName);

        combo.Type.Should().Be(ProductType.Menu, "only a Menu product produces bundle components");

        var components = combo.MenuDefinition!.Sections
            .SelectMany(s => s.Items)
            .Select(i => i.Product)
            .ToList();

        components.Should().HaveCount(2);
        // THE property the printer-app suite needs: one ticket per kitchen, from one combo.
        components.Select(p => p.KitchenType)
            .Should().BeEquivalentTo([KitchenType.FrontKitchen, KitchenType.BackKitchen],
                "a same-kitchen bundle cannot reproduce the routing bug this fixture exists for");
    }

    [Fact]
    public async Task Enabled_Twice_IsANoOp()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await E2EMenuFixtureSeeder.SeedAsync(context, NullLogger.Instance, new SeedSettings { SeedE2EMenuFixtures = true });
        await E2EMenuFixtureSeeder.SeedAsync(context, NullLogger.Instance, new SeedSettings { SeedE2EMenuFixtures = true });

        // Seeders run on EVERY boot, so a non-idempotent one would grow the menu on each deploy.
        (await context.Products.CountAsync(p => p.Name == ComboName)).Should().Be(1);
    }

    [Fact]
    public async Task Enabled_AfterTheFixtureWasSoftDeleted_IsStillANoOp()
    {
        // An admin tidying the staging menu soft-deletes the product; the row (and its
        // MenuDefinition) survive. A guard that respects the soft-delete filter would re-insert the
        // same primary keys, throw out of MigrateApplicationDatabaseAsync, and stop the app from
        // ever reaching app.Run() — a routine cleanup presenting as a crash loop.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await E2EMenuFixtureSeeder.SeedAsync(context, NullLogger.Instance, new SeedSettings { SeedE2EMenuFixtures = true });

        var combo = await context.Products.SingleAsync(p => p.Name == ComboName);
        combo.IsDeleted = true;
        await context.SaveChangesAsync();

        var act = async () => await E2EMenuFixtureSeeder.SeedAsync(
            context, NullLogger.Instance, new SeedSettings { SeedE2EMenuFixtures = true });

        await act.Should().NotThrowAsync("re-inserting the same primary keys would crash every boot");
    }

}
