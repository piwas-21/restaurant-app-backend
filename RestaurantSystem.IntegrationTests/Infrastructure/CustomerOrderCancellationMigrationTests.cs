using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed class CustomerOrderCancellationMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Previous = "20261020002402_" + nameof(AddCustomerCheckoutEffectRecovery);
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => MigrationTestSafety.RestoreCurrentSchemaAsync(fixture);

    [Fact]
    public async Task Empty_downgrade_preserves_prior_native_operations_and_upgrade_restores_current_schema()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(Previous);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var check = new NpgsqlCommand("""
                SELECT to_regclass('public.customer_order_cancellation_operations') IS NULL
                  AND to_regclass('public.customer_order_operations') IS NOT NULL
                  AND to_regclass('public.customer_order_checkout_journals') IS NOT NULL
                """, connection);
            (await check.ExecuteScalarAsync()).Should().Be(true);
            await migrator.MigrateAsync();
            (await context.Set<CustomerOrderCancellationOperation>().CountAsync()).Should().Be(0);
            (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(TestDatabaseCluster.CurrentSchemaMigration);
        }
        finally { await migrator.MigrateAsync(); }
    }

    [Theory]
    [InlineData(CustomerOrderCancellationOutcome.Cancelled)]
    [InlineData(CustomerOrderCancellationOutcome.Refused)]
    public async Task Every_terminal_decision_survives_independently_of_order_rows_and_blocks_downgrade(
        CustomerOrderCancellationOutcome outcome)
    {
        await using var context = fixture.CreateContext();
        var operation = Valid();
        operation.Outcome = outcome;
        operation.Reason = outcome == CustomerOrderCancellationOutcome.Refused
            ? CustomerOrderCancellationReason.FinancialReview : null;
        context.Set<CustomerOrderCancellationOperation>().Add(operation);
        await context.SaveChangesAsync();
        (await context.Orders.AnyAsync(order => order.Id == operation.OrderId)).Should().BeFalse();
        var downgrade = () => context.GetService<IMigrator>().MigrateAsync(Previous);
        var error = (await downgrade.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        error.MessageText.Should().Be("Customer order cancellation decisions must be retained");
        (await context.Set<CustomerOrderCancellationOperation>().SingleAsync()).OperationId.Should().Be(operation.OperationId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_decisions_cannot_be_rewritten_or_deleted(bool delete)
    {
        await using var context = fixture.CreateContext();
        var operation = Valid();
        context.Set<CustomerOrderCancellationOperation>().Add(operation);
        await context.SaveChangesAsync();
        var alter = async () =>
        {
            if (delete) await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM customer_order_cancellation_operations WHERE operation_id = {operation.OperationId}");
            else await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE customer_order_cancellation_operations SET observed_version = observed_version + 1 WHERE operation_id = {operation.OperationId}");
        };
        var error = (await alter.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        error.MessageText.Should().Be("Customer order cancellation decisions are immutable");
        (await context.Set<CustomerOrderCancellationOperation>().AsNoTracking().SingleAsync()).Should().BeEquivalentTo(operation);
    }

    [Theory]
    [InlineData("refused-without-reason")]
    [InlineData("unseen")]
    [InlineData("cancelled-with-reason")]
    [InlineData("same-version")]
    [InlineData("zero-order")]
    [InlineData("zero-operation")]
    [InlineData("bad-owner")]
    [InlineData("bad-payload")]
    [InlineData("zero-expected")]
    [InlineData("zero-observed")]
    [InlineData("unknown-reason")]
    [InlineData("unknown-outcome")]
    public async Task Database_refuses_unverified_or_contradictory_decision_shapes(string defect)
    {
        var operation = Valid();
        switch (defect)
        {
            case "refused-without-reason": operation.Outcome = CustomerOrderCancellationOutcome.Refused; break;
            case "unseen": operation.Outcome = CustomerOrderCancellationOutcome.Unseen; break;
            case "cancelled-with-reason": operation.Reason = CustomerOrderCancellationReason.FinancialReview; break;
            case "same-version": operation.ObservedVersion = operation.ExpectedVersion; break;
            case "zero-order": operation.OrderId = Guid.Empty; break;
            case "zero-operation": operation.OperationId = Guid.Empty; break;
            case "bad-owner": operation.OwnerHash = new string('x', 64); break;
            case "bad-payload": operation.PayloadHash = new string('B', 64); break;
            case "zero-expected": operation.ExpectedVersion = 0; break;
            case "zero-observed": operation.ObservedVersion = 0; break;
            case "unknown-reason":
                operation.Outcome = CustomerOrderCancellationOutcome.Refused;
                operation.Reason = (CustomerOrderCancellationReason)int.MaxValue;
                break;
            case "unknown-outcome": operation.Outcome = (CustomerOrderCancellationOutcome)int.MaxValue; break;
        }
        await using var context = fixture.CreateContext();
        context.Set<CustomerOrderCancellationOperation>().Add(operation);
        var save = () => context.SaveChangesAsync();
        var error = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        error.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_version_conflict_can_retain_the_lower_actual_version_without_changing_the_original_intent()
    {
        await using var context = fixture.CreateContext();
        var operation = Valid();
        operation.ExpectedVersion = 9;
        operation.ObservedVersion = 2;
        operation.Outcome = CustomerOrderCancellationOutcome.Refused;
        operation.Reason = CustomerOrderCancellationReason.OrderChanged;
        context.Set<CustomerOrderCancellationOperation>().Add(operation);
        await context.SaveChangesAsync();
        var stored = await context.Set<CustomerOrderCancellationOperation>().AsNoTracking().SingleAsync();
        stored.ExpectedVersion.Should().Be(9);
        stored.ObservedVersion.Should().Be(2);
    }

    [Fact]
    public async Task A_concurrent_receipt_commit_cannot_be_discarded_by_downgrade()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
        var migrator = context.GetService<IMigrator>();
        await using var writer = new NpgsqlConnection(fixture.ConnectionString);
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        var operationId = Guid.NewGuid();
        await using var insert = new NpgsqlCommand("""
            INSERT INTO customer_order_cancellation_operations
                (operation_id,order_id,owner_hash,payload_hash,expected_version,observed_version,outcome,reason,completed_at)
            VALUES (@operation,@order,repeat('a',64),repeat('b',64),1,2,'Cancelled',NULL,now())
            """, writer, transaction);
        insert.Parameters.AddWithValue("operation", operationId);
        insert.Parameters.AddWithValue("order", Guid.NewGuid());
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
            (await context.Set<CustomerOrderCancellationOperation>().AsNoTracking().SingleAsync()).OperationId.Should().Be(operationId);
        }
        finally
        {
            if (!committed) await transaction.RollbackAsync();
            try { await downgrade; } catch (PostgresException) { /* Expected preservation guard. */ }
            await migrator.MigrateAsync();
        }
    }

    [Fact]
    public async Task A_reused_operation_id_is_rejected_by_the_database()
    {
        var original = Valid();
        await using (var first = fixture.CreateContext())
        {
            first.Set<CustomerOrderCancellationOperation>().Add(original);
            await first.SaveChangesAsync();
        }
        await using var second = fixture.CreateContext();
        var duplicate = Valid();
        duplicate.OperationId = original.OperationId;
        second.Set<CustomerOrderCancellationOperation>().Add(duplicate);
        var save = () => second.SaveChangesAsync();
        var error = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        error.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public void Current_model_and_snapshot_agree_on_the_explicit_cancellation_column_mapping()
    {
        using var context = fixture.CreateContext();
        var entity = context.Model.FindEntityType(typeof(CustomerOrderCancellationOperation));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("customer_order_cancellation_operations");
        entity.FindProperty(nameof(CustomerOrderCancellationOperation.OwnerHash))!.GetColumnName()
            .Should().Be("owner_hash");
        context.Database.HasPendingModelChanges().Should().BeFalse();
    }

    private static CustomerOrderCancellationOperation Valid() => new()
    {
        OperationId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        OwnerHash = new string('a', 64),
        PayloadHash = new string('b', 64),
        ExpectedVersion = 1,
        ObservedVersion = 2,
        Outcome = CustomerOrderCancellationOutcome.Cancelled,
        // PostgreSQL timestamps retain microseconds; keep the strict whole-row oracle representable.
        CompletedAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc).AddTicks(1234560)
    };
}
