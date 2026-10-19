using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed class CustomerOrderOperationMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Previous = "20261019045735_" + nameof(AddCustomerBasketCancellation);
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => MigrationTestSafety.RestoreCurrentSchemaAsync(fixture);
    [Fact]
    public async Task Empty_downgrade_removes_only_order_operations_and_upgrade_restores_current_schema()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(Previous);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var check = new NpgsqlCommand("SELECT to_regclass('public.customer_order_operations') IS NULL AND to_regclass('public.customer_basket_operations') IS NOT NULL", connection);
            (await check.ExecuteScalarAsync()).Should().Be(true);
            await migrator.MigrateAsync();
            (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
            (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(TestDatabaseCluster.CurrentSchemaMigration);
        }
        finally { await migrator.MigrateAsync(); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Applied_and_cancelled_history_refuses_downgrade(bool cancelled)
    {
        await using var context = fixture.CreateContext();
        var operation = new CustomerOrderOperation
        {
            OperationId = Guid.NewGuid(),
            OwnerHash = new string('a', 64),
            PayloadHash = new string('b', 64),
            OrderId = cancelled ? null : Guid.NewGuid(),
            IsCancelled = cancelled,
            CompletedAt = DateTime.UtcNow
        };
        context.Set<CustomerOrderOperation>().Add(operation);
        await context.SaveChangesAsync();
        var downgrade = () => context.GetService<IMigrator>().MigrateAsync(Previous);
        var refusal = (await downgrade.Should().ThrowAsync<PostgresException>()).Which;
        refusal.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        refusal.MessageText.Should().Be("Customer order operation history must be retained");
        (await context.Set<CustomerOrderOperation>().AsNoTracking().SingleAsync()).OperationId.Should().Be(operation.OperationId);
        (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(TestDatabaseCluster.CurrentSchemaMigration);
    }
    [Fact]
    public async Task Concurrent_commit_is_observed_before_downgrade_can_discard_a_receipt()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
        var migrator = context.GetService<IMigrator>();
        await using var writer = new NpgsqlConnection(fixture.ConnectionString);
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        var id = Guid.NewGuid();
        await using var insert = new NpgsqlCommand("INSERT INTO customer_order_operations (operation_id,owner_hash,payload_hash,order_id,is_cancelled,completed_at) VALUES (@id,repeat('a',64),repeat('b',64),NULL,true,now())", writer, transaction);
        insert.Parameters.AddWithValue("id", id);
        await insert.ExecuteNonQueryAsync();
        var downgrade = migrator.MigrateAsync(Previous);
        var committed = false;
        try
        {
            (await MigrationTestSafety.WaitForBlockedMigrationAsync(fixture.ConnectionString, pid)).Should().BeTrue();
            await transaction.CommitAsync();
            committed = true;
            var refusal = async () => await downgrade;
            (await refusal.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            (await context.Set<CustomerOrderOperation>().AsNoTracking().SingleAsync()).OperationId.Should().Be(id);
        }
        finally
        {
            if (!committed) await transaction.RollbackAsync();
            try { await downgrade; } catch (PostgresException) { /* Expected preservation guard. */ }
            await migrator.MigrateAsync();
        }
    }
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Database_rejects_inconsistent_applied_and_cancelled_outcomes(bool cancelled, bool hasOrder)
    {
        await using var context = fixture.CreateContext();
        context.Set<CustomerOrderOperation>().Add(new()
        {
            OperationId = Guid.NewGuid(),
            OwnerHash = new string('a', 64),
            PayloadHash = new string('b', 64),
            IsCancelled = cancelled,
            OrderId = hasOrder ? Guid.Empty : null,
            CompletedAt = DateTime.UtcNow
        });
        var save = () => context.SaveChangesAsync();
        var error = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        error.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }
}
