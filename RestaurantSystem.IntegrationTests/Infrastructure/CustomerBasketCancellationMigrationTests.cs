using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed class CustomerBasketCancellationMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20261019001953_" + nameof(AddCustomerBasketOperations);
    // EF may commit removal of newer empty tables before reaching this migration's receipt guard.
    private const string RefusingMigration = "20261019045735_" + nameof(AddCustomerBasketCancellation);
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => MigrationTestSafety.RestoreCurrentSchemaAsync(fixture);

    [Fact]
    public async Task Existing_receipts_upgrade_as_applied_and_the_guard_preserves_them_on_downgrade()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(PreviousMigration);
            var id = Guid.NewGuid();
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand("INSERT INTO customer_basket_operations (operation_id,owner_hash,payload_hash,basket_id,applied_at) VALUES (@id,repeat('a',64),repeat('b',64),@basket,now())", connection);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("basket", Guid.NewGuid());
            await insert.ExecuteNonQueryAsync();
            await migrator.MigrateAsync();
            var original = await context.Set<CustomerBasketOperation>().AsNoTracking().SingleAsync();
            original.OperationId.Should().Be(id);
            original.IsCancelled.Should().BeFalse();
            var refusal = async () => await migrator.MigrateAsync(PreviousMigration);
            (await refusal.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(RefusingMigration);
        }
        finally { await migrator.MigrateAsync(); }
    }

    [Fact]
    public async Task Empty_downgrade_can_remove_the_new_column_and_upgrade_restores_it()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(PreviousMigration);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var check = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='customer_basket_operations' AND column_name='is_cancelled'", connection);
            (await check.ExecuteScalarAsync()).Should().Be(0L);
            (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(PreviousMigration);
            await migrator.MigrateAsync();
            (await check.ExecuteScalarAsync()).Should().Be(1L);
            (await context.Set<CustomerBasketOperation>().CountAsync()).Should().Be(0);
        }
        finally { await migrator.MigrateAsync(); }
    }

    [Fact]
    public async Task A_concurrent_cancellation_commit_cannot_be_erased_by_downgrade()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
        var migrator = context.GetService<IMigrator>();
        await using var writer = new NpgsqlConnection(fixture.ConnectionString);
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        var id = Guid.NewGuid();
        await using var insert = new NpgsqlCommand("INSERT INTO customer_basket_operations (operation_id,owner_hash,payload_hash,basket_id,is_cancelled,applied_at) VALUES (@id,repeat('a',64),repeat('b',64),@basket,true,now())", writer, transaction);
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("basket", Guid.Empty);
        await insert.ExecuteNonQueryAsync();
        var downgrade = migrator.MigrateAsync(PreviousMigration);
        var committed = false;
        try
        {
            (await MigrationTestSafety.WaitForBlockedMigrationAsync(fixture.ConnectionString, pid)).Should().BeTrue();
            await transaction.CommitAsync();
            committed = true;
            var refusal = async () => await downgrade;
            (await refusal.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            var saved = await context.Set<CustomerBasketOperation>().AsNoTracking().SingleAsync();
            saved.OperationId.Should().Be(id);
            saved.IsCancelled.Should().BeTrue();
            saved.BasketId.Should().BeEmpty();
            (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(RefusingMigration);
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
