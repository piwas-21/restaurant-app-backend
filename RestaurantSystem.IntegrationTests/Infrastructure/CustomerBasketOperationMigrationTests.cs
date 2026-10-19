using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed class CustomerBasketOperationMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20261016224309_" + nameof(AddStaffBillCharges);
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => MigrationTestSafety.RestoreCurrentSchemaAsync(fixture);

    [Fact]
    public async Task Empty_downgrade_removes_only_operation_table_and_upgrade_restores_current_schema()
    {
        await using var context = fixture.CreateContext();
        (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(TestDatabaseCluster.CurrentSchemaMigration);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(PreviousMigration);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var check = new NpgsqlCommand("SELECT to_regclass('public.customer_basket_operations') IS NULL AND to_regclass('public.\"Baskets\"') IS NOT NULL", connection);
        (await check.ExecuteScalarAsync()).Should().Be(true);
        await migrator.MigrateAsync();
        (await context.Set<CustomerBasketOperation>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Saved_receipt_refuses_downgrade_and_preserves_operation_identity()
    {
        await using var context = fixture.CreateContext();
        var operation = new CustomerBasketOperation
        {
            OperationId = Guid.NewGuid(),
            BasketId = Guid.NewGuid(),
            OwnerHash = new string('a', 64),
            PayloadHash = new string('b', 64),
            AppliedAt = DateTime.UtcNow
        };
        context.Set<CustomerBasketOperation>().Add(operation);
        await context.SaveChangesAsync();
        var downgrade = () => context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var refusal = (await downgrade.Should().ThrowAsync<PostgresException>()).Which;
        refusal.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        refusal.MessageText.Should().Be("Customer basket operation history must be retained");
        (await context.Set<CustomerBasketOperation>().AsNoTracking().SingleAsync()).OperationId.Should().Be(operation.OperationId);
        (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(TestDatabaseCluster.CurrentSchemaMigration);
    }

    [Fact]
    public async Task Concurrent_receipt_commit_is_seen_before_downgrade_can_erase_identity()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
        var migrator = context.GetService<IMigrator>();
        await using var writer = new NpgsqlConnection(fixture.ConnectionString);
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        var operationId = Guid.NewGuid();
        await using var write = new NpgsqlCommand("INSERT INTO customer_basket_operations (operation_id,owner_hash,payload_hash,basket_id,applied_at) VALUES (@id,repeat('a',64),repeat('b',64),@basket,now())", writer, transaction);
        write.Parameters.AddWithValue("id", operationId);
        write.Parameters.AddWithValue("basket", Guid.NewGuid());
        await write.ExecuteNonQueryAsync();
        var downgrade = migrator.MigrateAsync(PreviousMigration);
        var committed = false;
        try
        {
            (await MigrationTestSafety.WaitForBlockedMigrationAsync(fixture.ConnectionString, pid)).Should().BeTrue();
            await transaction.CommitAsync();
            committed = true;
            var refusal = async () => await downgrade;
            (await refusal.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            (await context.Set<CustomerBasketOperation>().AsNoTracking().SingleAsync()).OperationId.Should().Be(operationId);
        }
        finally
        {
            if (!committed) await transaction.RollbackAsync();
            try { await downgrade; }
            catch (PostgresException) { /* Expected preservation guard. */ }
            await migrator.MigrateAsync();
        }
    }
}
