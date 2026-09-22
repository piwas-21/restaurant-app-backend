using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class OrderRoutingRequirementMigrationTests
{
    private const string MigrationBeforeRequirement =
        "20260921194517" + "_AddOrderRoutingStates";

    private readonly DatabaseFixture _fixture;

    public OrderRoutingRequirementMigrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migration_backfills_historical_cashier_routes_as_optional_and_kitchen_as_required()
    {
        var orderId = Guid.NewGuid();
        var cashierRouteId = Guid.NewGuid();
        var kitchenRouteId = Guid.NewGuid();
        var cashierJobId = Guid.NewGuid();
        var kitchenJobId = Guid.NewGuid();

        try
        {
            await using (var setup = _fixture.CreateContext())
            {
                // Revert the real lane database to the schema immediately before the migration.
                // The order and route rows below therefore exist while is_required is absent.
                await setup.Database.MigrateAsync(MigrationBeforeRequirement);
                await TestOrderSeeder.SeedOrderAsync(setup, orderId);

                // The route FK points at a real order entity; every non-nullable historical
                // routing column is supplied exactly as the pre-migration schema requires.
                await setup.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "OrderRoutingStates"
                        (id, order_id, job_id, revision, version, target, status, created_by)
                    VALUES
                        ({cashierRouteId}, {orderId}, {cashierJobId}, 1, 1, 'Cashier', 'Queued',
                            'migration-test'),
                        ({kitchenRouteId}, {orderId}, {kitchenJobId}, 1, 1, 'General', 'Queued',
                            'migration-test');
                    """);
            }

            await using (var migrate = _fixture.CreateContext())
            {
                // Apply the shipped migration, including its actual PostgreSQL UPDATE statement.
                await migrate.Database.MigrateAsync();

                var routes = await migrate.OrderRoutingStates
                    .Where(route => route.OrderId == orderId)
                    .ToListAsync();

                routes.Should().HaveCount(2);
                routes.Single(route => route.Target == DevicePrintTarget.Cashier)
                    .IsRequired.Should().BeFalse();
                routes.Single(route => route.Target == DevicePrintTarget.General)
                    .IsRequired.Should().BeTrue();
            }
        }
        finally
        {
            // Restore the lane to the latest schema even if an assertion fails, then remove only
            // this test's FK-linked rows so other Database Lane 4 tests retain their data.
            await using var cleanup = _fixture.CreateContext();
            await cleanup.Database.MigrateAsync();
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "OrderRoutingStates" WHERE "order_id" = {orderId};
                """);
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM order_changes WHERE order_id = {orderId};
                """);
            await cleanup.Database.ExecuteSqlRawAsync(
                "ALTER TABLE orders DISABLE TRIGGER orders_queue_sequence;");
            try
            {
                await cleanup.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM orders WHERE id = {orderId};
                    """);
            }
            finally
            {
                await cleanup.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE orders ENABLE TRIGGER orders_queue_sequence;");
            }
        }
    }
}
