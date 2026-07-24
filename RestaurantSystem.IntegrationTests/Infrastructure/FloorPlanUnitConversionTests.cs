using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Infrastructure.Persistence.Support;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// FLOOR-PLAN-REVAMP §6 unit → metre conversion. Runs the <em>exact</em> SQL the
/// AddFloorPlanAggregate migration uses (<see cref="FloorPlanMigrationSql"/>)
/// against real Postgres over hand-seeded legacy pixel rows — the migration
/// itself never fires on the test DB (Tables is empty at migration time), so the
/// conversion arithmetic, the Width=0 seats-derived fallback, the [0.40, 4.00]
/// clamp and the shape remap are proven here.
/// </summary>
public class FloorPlanUnitConversionTests : IntegrationTestBase
{
    public FloorPlanUnitConversionTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task Convert_LegacyPixelTables_ToMetres_ClampsFallsBackAndRemapsShape()
    {
        Guid planId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var plan = new Domain.Entities.FloorPlan
            {
                Name = "Main floor",
                WidthMeters = FloorPlanMigrationSql.RoomWidthMeters,
                HeightMeters = FloorPlanMigrationSql.RoomHeightMeters,
                IsDefault = true,
                CreatedBy = "seed",
            };
            db.FloorPlans.Add(plan);

            db.Tables.AddRange(
                // number, seats, x, y, width, height, shape (all in legacy pixels)
                Legacy("A", 4, 300, 250, 100, 100, "circle"),     // normal + circle→round
                Legacy("B", 6, 60, 100, 1, 1, ""),                // width→0 below (clobber); seats fallback; ''→round
                Legacy("C", 2, 600, 500, 60, 60, "square"),       // max coords; square kept
                Legacy("D", 4, 30, 30, 15, 10, "rectangle"),      // below-floor size → clamp up
                Legacy("E", 8, 300, 250, 250, 60, "rectangle"));  // above-ceiling width → clamp down
            await db.SaveChangesAsync();
            planId = plan.Id;

            // Reproduce the defect-8 clobber (width/height zeroed via UPDATE, not
            // INSERT — an INSERT of 0 hits the HasDefaultValue(80) sentinel and
            // the store default is written instead).
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"Tables\" SET width = 0, height = 0 WHERE table_number = 'B';");

            var sql = FloorPlanMigrationSql.ConvertTablesToMetresTemplate
                .Replace("{PLAN_ID}", $"'{planId}'::uuid");
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tables = await db.Tables.AsNoTracking().ToDictionaryAsync(t => t.TableNumber);

            // A — straight scale: 300/600*12, 250/500*10, 100/600*12, 100/500*10.
            AssertGeometry(tables["A"], 6.00m, 5.00m, 2.00m, 2.00m, "round", planId);
            // B — clobbered (width 0): 6-seat fallback 1.80×0.90; blank shape → round.
            AssertGeometry(tables["B"], 1.20m, 2.00m, 1.80m, 0.90m, "round", planId);
            // C — the far corner maps onto the room corner exactly.
            AssertGeometry(tables["C"], 12.00m, 10.00m, 1.20m, 1.20m, "square", planId);
            // D — 15px→0.30 and 10px→0.20 both clamp up to the 0.40 floor.
            AssertGeometry(tables["D"], 0.60m, 0.60m, 0.40m, 0.40m, "rectangle", planId);
            // E — 250px→5.00 clamps down to the 4.00 ceiling; height unclamped.
            AssertGeometry(tables["E"], 6.00m, 5.00m, 4.00m, 1.20m, "rectangle", planId);
        }
    }

    private static void AssertGeometry(Table t, decimal x, decimal y, decimal w, decimal h, string shape, Guid planId)
    {
        t.PositionX.Should().Be(x);
        t.PositionY.Should().Be(y);
        t.Width.Should().Be(w);
        t.Height.Should().Be(h);
        t.Shape.Should().Be(shape);
        t.FloorPlanId.Should().Be(planId);
    }

    private static Table Legacy(string number, int seats, decimal x, decimal y, decimal width, decimal height, string shape) => new()
    {
        TableNumber = number,
        MaxGuests = seats,
        IsActive = true,
        PositionX = x,
        PositionY = y,
        Width = width,
        Height = height,
        Shape = shape,
        FloorPlanId = null,
        CreatedBy = "seed",
    };
}
