using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed class CustomerTablePlanOperationMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Previous = "20261023015615_" + nameof(AddNativeTableCheckoutScope);
    private const string ProtectedMigration = "20261023085828_" + nameof(AddCustomerTablePlanOperations);
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => MigrationTestSafety.RestoreCurrentSchemaAsync(fixture);

    [Fact]
    public async Task Empty_downgrade_and_upgrade_preserve_the_shared_payment_ledger()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var check = new NpgsqlCommand("SELECT to_regclass('public.customer_table_plan_operations') IS NULL AND to_regclass('public.account_payment_attempts') IS NOT NULL AND to_regclass('public.customer_order_operations') IS NOT NULL", connection);
        (await check.ExecuteScalarAsync()).Should().Be(true);
        await migrator.MigrateAsync();
        (await context.Set<CustomerTablePlanOperation>().CountAsync()).Should().Be(0);
        (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(TestDatabaseCluster.CurrentSchemaMigration);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stopped_and_applied_split_decisions_both_block_downgrade(bool stopped)
    {
        await using var context = fixture.CreateContext();
        var operation = new CustomerTablePlanOperation
        {
            OperationId = Guid.NewGuid(),
            OwnerHash = new string('a', 64),
            PayloadHash = new string('b', 64),
            PlanId = stopped ? null : Guid.NewGuid(),
            IsStopped = stopped,
            CompletedAt = DateTime.UtcNow
        };
        context.Set<CustomerTablePlanOperation>().Add(operation);
        await context.SaveChangesAsync();
        var downgrade = () => context.GetService<IMigrator>().MigrateAsync(Previous);
        (await downgrade.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        (await context.Set<CustomerTablePlanOperation>().SingleAsync()).OperationId.Should().Be(operation.OperationId);
        // Newer empty migrations can legitimately roll back before this protected migration refuses.
        (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(ProtectedMigration);
    }

    [Fact]
    public async Task In_flight_decision_refuses_downgrade_without_waiting_and_remains_after_commit()
    {
        await using var context = fixture.CreateContext();
        await using var writer = new NpgsqlConnection(fixture.ConnectionString);
        await writer.OpenAsync(); await using var transaction = await writer.BeginTransactionAsync();
        await using var insert = new NpgsqlCommand("INSERT INTO customer_table_plan_operations (operation_id,owner_hash,payload_hash,is_stopped,completed_at) VALUES (gen_random_uuid(),repeat('a',64),repeat('b',64),true,now())", writer, transaction);
        await insert.ExecuteNonQueryAsync();
        var downgrade = () => context.GetService<IMigrator>().MigrateAsync(Previous);
        var refusal = (await downgrade.Should().ThrowAsync<InvalidOperationException>()).Which;
        refusal.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.LockNotAvailable);
        await transaction.CommitAsync();
        (await context.Set<CustomerTablePlanOperation>().CountAsync()).Should().Be(1);
        (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(ProtectedMigration);
    }
}
