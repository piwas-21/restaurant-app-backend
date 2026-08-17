using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Infrastructure.Persistence.Seeders;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// FLOOR-PLAN-REVAMP S3 seeders: on a fresh database <see cref="FloorPlanSeeder"/>
/// builds the reference 14×9 m plan (Main room + Terrace, doors/windows, decor)
/// and <see cref="TableSeeder"/> seeds the 18 reference tables in metres, linked
/// to that plan. Both are idempotent. The base fixture resets FloorPlans/Tables
/// between tests, so each starts from empty.
/// </summary>
[Collection("Database Lane 1")]
public class FloorPlanSeederTests : IntegrationTestBase
{
    public FloorPlanSeederTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task Seed_FreshDatabase_CreatesReferencePlan()
    {
        await RunFloorPlanSeederAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var plan = await db.FloorPlans
            .Include(p => p.Walls).ThenInclude(w => w.Openings)
            .Include(p => p.Items)
            .AsNoTracking()
            .SingleAsync(p => p.IsDefault);

        plan.WidthMeters.Should().Be(14.00m);
        plan.HeightMeters.Should().Be(9.00m);
        plan.GridSizeCm.Should().Be(25);
        plan.Walls.Should().HaveCount(2);
        plan.Walls.SelectMany(w => w.Openings).Should().HaveCount(6);
        plan.Walls.Should().Contain(w => w.RoomName == "Terrace" && w.FloorStyle == "deck");
        plan.Items.Should().HaveCount(16);
        plan.Items.Should().Contain(i => i.Kind == "entrance");
        plan.Items.Should().Contain(i => i.Kind == "label" && i.Label == "Bar");
    }

    [Fact]
    public async Task Seed_SecondRun_IsNoOp()
    {
        await RunFloorPlanSeederAsync();
        await RunFloorPlanSeederAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.FloorPlans.CountAsync()).Should().Be(1);
        (await db.FloorPlanWalls.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task TableSeeder_SeedsMetreTables_LinkedToDefaultPlan()
    {
        await RunFloorPlanSeederAsync();
        await RunTableSeederAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var planId = await db.FloorPlans.Where(p => p.IsDefault).Select(p => p.Id).SingleAsync();
        var tables = await db.Tables.AsNoTracking().ToListAsync();

        tables.Should().HaveCount(18);
        tables.Should().OnlyContain(t => t.FloorPlanId == planId);

        // Metres, not pixels: the biggest table is a rect-8 at 2.3×0.95 m.
        var t9 = tables.Single(t => t.TableNumber == "9");
        t9.MaxGuests.Should().Be(8);
        t9.Shape.Should().Be("rectangle");
        t9.Width.Should().Be(2.30m);
        t9.PositionX.Should().Be(5.20m);

        // Terrace tables (the "11a".."14b" suffixed numbers) are outdoor; the
        // plain-numbered Main-room tables are not.
        tables.Where(t => t.TableNumber.Length == 3).Should().OnlyContain(t => t.IsOutdoor);
        tables.Single(t => t.TableNumber == "1").IsOutdoor.Should().BeFalse();
    }

    private async Task RunFloorPlanSeederAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await FloorPlanSeeder.SeedAsync(db, NullLogger.Instance);
    }

    private async Task RunTableSeederAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await TableSeeder.SeedAsync(db, NullLogger.Instance);
    }
}
