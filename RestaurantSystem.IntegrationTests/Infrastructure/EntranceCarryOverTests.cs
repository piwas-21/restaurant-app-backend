using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Infrastructure.Persistence.Support;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// FLOOR-PLAN-REVAMP §6 step 4 (slice S10) — carrying a stored
/// <c>RestaurantInfo.entrance_position_x/y</c> onto the plan as an
/// <c>entrance</c> item before those columns are dropped.
///
/// Runs the <em>exact</em> SQL the <c>RetireRestaurantInfoEntrancePosition</c>
/// migration uses. The migration itself can never fire meaningfully on the test
/// DB — the columns are created and dropped in the same `dotnet ef database
/// update`, with nothing in them — so the arithmetic and, more importantly, the
/// **"don't create a second entrance"** guard are proven here or nowhere.
///
/// The columns are gone from the model by the time this runs, so the fixture
/// writes them with raw SQL against the pre-drop shape.
/// </summary>
public class EntranceCarryOverTests : IntegrationTestBase
{
    public EntranceCarryOverTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private const string AddColumns = @"
ALTER TABLE ""RestaurantInfo"" ADD COLUMN IF NOT EXISTS entrance_position_x numeric(5,2);
ALTER TABLE ""RestaurantInfo"" ADD COLUMN IF NOT EXISTS entrance_position_y numeric(5,2);";

    private const string DropColumns = @"
ALTER TABLE ""RestaurantInfo"" DROP COLUMN IF EXISTS entrance_position_x;
ALTER TABLE ""RestaurantInfo"" DROP COLUMN IF EXISTS entrance_position_y;";

    [Fact]
    public async Task CarriesAStoredPercentPositionOntoThePlanInMetres()
    {
        var planId = await ArrangeAsync(entranceX: 25m, entranceY: 80m, seedPlanEntrance: false);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var items = await db.FloorPlanItems.AsNoTracking()
            .Where(i => i.FloorPlanId == planId && i.Kind == "entrance")
            .ToListAsync();

        items.Should().ContainSingle();
        // 25 % of a 12 m plan and 80 % of a 10 m plan.
        items[0].X.Should().Be(3.00m);
        items[0].Y.Should().Be(8.00m);
        items[0].WidthMeters.Should().Be(FloorPlanMigrationSql.EntranceWidthMeters);
        items[0].HeightMeters.Should().Be(FloorPlanMigrationSql.EntranceHeightMeters);
    }

    [Fact]
    public async Task ClampsAnOutOfRangePercentRatherThanPlacingItOffPlan()
    {
        var planId = await ArrangeAsync(entranceX: 140m, entranceY: -20m, seedPlanEntrance: false);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var item = await db.FloorPlanItems.AsNoTracking()
            .SingleAsync(i => i.FloorPlanId == planId && i.Kind == "entrance");

        item.X.Should().Be(12.00m);
        item.Y.Should().Be(0.00m);
    }

    /// <summary>
    /// The seeded plan already carries an entrance. A second one placed from the
    /// legacy percentages would put two arrows in different places — the exact
    /// defect §4.4 called out when it made the wall opening draw the doorway and
    /// the marker draw only an arrow.
    /// </summary>
    [Fact]
    public async Task LeavesAPlanThatAlreadyHasAnEntranceAlone()
    {
        var planId = await ArrangeAsync(entranceX: 25m, entranceY: 80m, seedPlanEntrance: true);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var items = await db.FloorPlanItems.AsNoTracking()
            .Where(i => i.FloorPlanId == planId && i.Kind == "entrance")
            .ToListAsync();

        items.Should().ContainSingle();
        items[0].X.Should().Be(6.60m, "the plan's own entrance is untouched");
    }

    [Fact]
    public async Task WritesNothingWhenNoPositionWasEverStored()
    {
        var planId = await ArrangeAsync(entranceX: null, entranceY: null, seedPlanEntrance: false);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var items = await db.FloorPlanItems.AsNoTracking()
            .Where(i => i.FloorPlanId == planId && i.Kind == "entrance")
            .ToListAsync();

        items.Should().BeEmpty();
    }

    /// <summary>
    /// Recreate the pre-drop shape, seed a plan and the legacy percentages, run
    /// the migration's SQL, then drop the columns again so the next test starts
    /// from the shipped schema.
    /// </summary>
    private async Task<Guid> ArrangeAsync(decimal? entranceX, decimal? entranceY, bool seedPlanEntrance)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Only the default plan is considered, so leave exactly one behind.
        db.FloorPlanItems.RemoveRange(await db.FloorPlanItems.ToListAsync());
        db.FloorPlans.RemoveRange(await db.FloorPlans.ToListAsync());
        await db.SaveChangesAsync();

        var plan = new Domain.Entities.FloorPlan
        {
            Name = "Main floor",
            WidthMeters = 12m,
            HeightMeters = 10m,
            IsDefault = true,
            CreatedBy = "seed",
        };
        db.FloorPlans.Add(plan);
        if (seedPlanEntrance)
        {
            db.FloorPlanItems.Add(new FloorPlanItem
            {
                FloorPlan = plan,
                Kind = "entrance",
                X = 6.60m,
                Y = 8.20m,
                WidthMeters = 0.90m,
                HeightMeters = 0.60m,
                RotationDegrees = 270m,
                CreatedBy = "seed",
            });
        }
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(AddColumns);
        if (entranceX is { } x && entranceY is { } y)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"RestaurantInfo\" SET entrance_position_x = {0}, entrance_position_y = {1};", x, y);
        }
        // Otherwise the freshly added columns are already NULL — which IS the
        // "no position was ever stored" case. Parameterising a null through
        // ExecuteSqlRaw is what EF has no store mapping for.

        await db.Database.ExecuteSqlRawAsync(FloorPlanMigrationSql.CarryEntranceToPlanTemplate);
        await db.Database.ExecuteSqlRawAsync(DropColumns);

        return plan.Id;
    }
}
