using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class OrderBillingSnapshotMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20261004105734_AddCashHistoryCapacityRefusal"; // pragma: allowlist secret
    private const string SnapshotMigration = "20261004172810_AddNativeOrderBillingSnapshots"; // pragma: allowlist secret
    private static readonly string[] SnapshotTables =
    ["order_billing_snapshots", "order_billing_snapshot_units", "order_billing_snapshot_owner_links"];

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_snapshot_rollback_removes_tables_and_source_guards_and_upgrade_restores_them()
    {
        try
        {
            await AssertSchemaAsync(present: true);
            await MigrateAsync(PreviousMigration);
            await AssertSchemaAsync(present: false);
            await MigrateAsync();
            await AssertSchemaAsync(present: true);
        }
        finally { await MigrateAsync(); }
    }

    [Fact]
    public async Task Snapshot_history_refuses_rollback_and_retains_its_source_and_unit_money()
    {
        try
        {
            var orderId = Guid.NewGuid();
            var acceptedAt = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
            var order = new Order
            {
                Id = orderId,
                OrderNumber = $"MIG-SNAP-{Guid.NewGuid():N}"[..20],
                Type = OrderType.Takeaway,
                Status = OrderStatus.Completed,
                SubTotal = 1m,
                Total = 1m,
                OrderDate = acceptedAt,
                CreatedAt = acceptedAt,
                CreatedBy = "snapshot-migration-test",
                Items = [new OrderItem
                {
                    Id = Guid.NewGuid(), OrderId = orderId, Quantity = 1,
                    ProductName = "Retained accepted unit", UnitPrice = 1m, ItemTotal = 1m,
                    CreatedAt = acceptedAt, CreatedBy = "snapshot-migration-test"
                }]
            };
            await using (var seed = fixture.CreateContext())
            {
                seed.Orders.Add(order);
                await seed.SaveChangesAsync();
                var snapshot = OrderBillingSnapshotFactory.Build(order, "CHF", null, null, 1_000);
                seed.OrderBillingSnapshots.Add(snapshot.Header);
                seed.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
                await seed.SaveChangesAsync();
            }

            var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(PreviousMigration));
            failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            failure.MessageText.Should().Be("Native order billing snapshot history must be retained");
            await AssertSchemaAsync(present: true);
            await using var verify = fixture.CreateContext();
            (await verify.Database.GetAppliedMigrationsAsync()).Last().Should().Be(SnapshotMigration);
            (await verify.Orders.AsNoTracking().SingleAsync(value => value.Id == orderId)).Total.Should().Be(1m);
            (await verify.OrderBillingSnapshots.AsNoTracking().SingleAsync()).TotalMinor.Should().Be(100);
            (await verify.OrderBillingSnapshotUnits.AsNoTracking().SingleAsync()).PayableFoodMinor.Should().Be(100);
        }
        finally { await MigrateAsync(); }
    }

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync(target);
    }

    private async Task AssertSchemaAsync(bool present)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var table in SnapshotTables)
        {
            await using var command = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", connection);
            command.Parameters.AddWithValue("table", table);
            ((bool)(await command.ExecuteScalarAsync())!).Should().Be(present, table);
        }
        await using var guard = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_proc WHERE proname = 'protect_order_billing_snapshot_item_fact_update')",
            connection);
        ((bool)(await guard.ExecuteScalarAsync())!).Should().Be(present, "the source graph guard follows the migration");
    }
}
