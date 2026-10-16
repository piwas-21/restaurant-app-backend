using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed class StaffBillChargeMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(5, "[]")]
    [InlineData(0, "[{\"amount\":5}]")]
    public async Task Saved_charge_money_or_audit_refuses_downgrade_and_preserves_evidence(int amount, string audit)
    {
        await using var context = fixture.CreateContext();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"CHARGE-{Guid.NewGuid():N}"[..20],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            Total = 20m,
            RemainingAmount = 20m + amount,
            BillChargeAmount = amount,
            BillChargesJson = audit,
            OrderDate = DateTime.UtcNow,
            CreatedBy = nameof(StaffBillChargeMigrationTests)
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var downgrade = () => context.GetService<IMigrator>().MigrateAsync("20261016175405_StaffOrderSimplification");
        var failure = (await downgrade.Should().ThrowAsync<PostgresException>()).Which;
        failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        failure.MessageText.Should().Be("Staff bill charge history must be retained");
        var retained = await context.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id);
        retained.BillChargeAmount.Should().Be(amount);
        retained.BillChargesJson.Should().Be(audit);
        (await context.Database.GetAppliedMigrationsAsync()).Should().Contain(TestDatabaseCluster.CurrentSchemaMigration);
    }
    [Fact]
    public async Task Concurrent_charge_commit_is_seen_before_downgrade_can_remove_columns()
    {
        await using var context = fixture.CreateContext();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"CONCURRENT-{Guid.NewGuid():N}"[..20],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            Total = 20m,
            RemainingAmount = 20m,
            OrderDate = DateTime.UtcNow,
            CreatedBy = nameof(StaffBillChargeMigrationTests)
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        await context.Database.OpenConnectionAsync();
        var migrationPid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
        var migrator = context.GetService<IMigrator>();
        await using var writer = new NpgsqlConnection(fixture.ConnectionString);
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        await using var write = new NpgsqlCommand(
            "UPDATE orders SET bill_charge_amount = 5, bill_charges_json = '[{\"amount\":5}]' WHERE id = @id", writer, transaction);
        write.Parameters.AddWithValue("id", order.Id);
        await write.ExecuteNonQueryAsync();
        var downgrade = migrator.MigrateAsync("20261016175405_StaffOrderSimplification");
        var committed = false;
        try
        {
            (await WaitForBlockedMigrationAsync(migrationPid)).Should().BeTrue(
                "the migration must reach a lock barrier while the fee write remains uncommitted");
            await transaction.CommitAsync();
            committed = true;
            var refusal = async () => await downgrade;
            (await refusal.Should().ThrowAsync<PostgresException>()).Which.SqlState
                .Should().Be(PostgresErrorCodes.CheckViolation);
            (await context.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id))
                .BillChargeAmount.Should().Be(5m);
        }
        finally
        {
            if (!committed) await transaction.RollbackAsync();
            try { await downgrade; }
            catch (PostgresException) { /* The expected preservation guard may have refused downgrade. */ }
            await migrator.MigrateAsync();
        }
    }

    private async Task<bool> WaitForBlockedMigrationAsync(int pid)
    {
        await using var monitor = new NpgsqlConnection(fixture.ConnectionString);
        await monitor.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var check = new NpgsqlCommand(
                "SELECT wait_event_type = 'Lock' FROM pg_stat_activity WHERE pid = @pid", monitor);
            check.Parameters.AddWithValue("pid", pid);
            if (await check.ExecuteScalarAsync() is true) return true;
            await Task.Delay(50);
        }
        return false;
    }

}
