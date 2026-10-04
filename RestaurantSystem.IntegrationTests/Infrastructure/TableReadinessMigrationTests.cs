using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class TableReadinessMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20261003090319_AddOrderBillingCredits";
    private const string ReadinessMigration = "20261003120021_AddTableVisitReadiness";
    private const string LatestMigration = "20261004105734_AddCashHistoryCapacityRefusal"; // pragma: allowlist secret

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unused_rollback_and_upgrade_preserve_table_identity_and_require_explicit_readiness()
    {
        var tableId = await SeedTableAsync();
        try
        {
            await MigrateAsync(PreviousMigration);
            await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
                    SELECT table_number, NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'Tables' AND column_name = 'readiness_version')
                    FROM "Tables" WHERE id = @id
                    """, connection);
                command.Parameters.AddWithValue("id", tableId);
                await using var reader = await command.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue("the historical table must still exist");
                reader.GetString(0).Should().Be("MIG-READY");
                reader.GetBoolean(1).Should().BeTrue("Down must actually remove the new column");
            }
            await MigrateAsync();
            await using var verify = fixture.CreateContext();
            var table = await verify.Tables.SingleAsync(value => value.Id == tableId);
            table.TableNumber.Should().Be("MIG-READY");
            table.ReadinessVersion.Should().Be(1);
            table.ReadinessState.Should().Be(TableReadinessState.NeedsReset);
        }
        finally { await MigrateAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rollback_refuses_used_readiness_even_when_the_table_needs_reset(bool withOperation)
    {
        (await LatestAppliedMigrationAsync()).Should().Be(LatestMigration);
        var tableId = await SeedTableAsync();
        try
        {
            await using (var context = fixture.CreateContext())
            {
                if (withOperation)
                    context.TableReadyOperations.Add(NewOperation(tableId, succeeded: false));
                else
                {
                    var table = await context.Tables.SingleAsync(value => value.Id == tableId);
                    table.ReadinessVersion = 2;
                }
                await context.SaveChangesAsync();
            }
            var rollback = () => MigrateAsync(PreviousMigration);
            (await rollback.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            (await LatestAppliedMigrationAsync()).Should().Be(ReadinessMigration,
                "later empty migrations roll back before the readiness guard rejects its own Down");
            await using var verify = fixture.CreateContext();
            var retained = await verify.Tables.SingleAsync(value => value.Id == tableId);
            retained.ReadinessState.Should().Be(TableReadinessState.NeedsReset);
            retained.ReadinessVersion.Should().Be(withOperation ? 1 : 2);
            (await verify.TableReadyOperations.CountAsync()).Should().Be(withOperation ? 1 : 0);
        }
        finally
        {
            await MigrateAsync();
        }
        (await LatestAppliedMigrationAsync()).Should().Be(LatestMigration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sql_cannot_update_or_delete_a_readiness_receipt(bool delete)
    {
        var operationId = await SeedOperationAsync();
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(delete
                ? "DELETE FROM table_ready_operations WHERE id = @id"
                : "UPDATE table_ready_operations SET created_by = 'legal-shape-change' WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", operationId);
            var mutate = async () => await command.ExecuteNonQueryAsync();
            var exception = (await mutate.Should().ThrowAsync<PostgresException>()).Which;
            exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            exception.MessageText.Should().Be("Table readiness operation history is immutable");
        }
        await using var verify = fixture.CreateContext();
        var retained = await verify.TableReadyOperations.SingleAsync(value => value.Id == operationId);
        retained.CreatedBy.Should().Be(nameof(TableReadinessMigrationTests));
        retained.ActorRole.Should().Be(UserRole.Server);
        retained.OutcomeReadinessVersion.Should().Be(2);
    }

    [Fact]
    public async Task Ef_refuses_a_shape_valid_change_to_persisted_actor_role()
    {
        var operationId = await SeedOperationAsync();
        await using var context = fixture.CreateContext();
        var operation = await context.TableReadyOperations.SingleAsync(value => value.Id == operationId);
        operation.ActorRole = UserRole.Cashier;
        var save = () => context.SaveChangesAsync();
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*read-only*");
        await using var verify = fixture.CreateContext();
        (await verify.TableReadyOperations.SingleAsync(value => value.Id == operationId))
            .ActorRole.Should().Be(UserRole.Server);
    }

    [Fact]
    public async Task Failed_receipt_requires_an_explicit_error_instead_of_sql_null_check_bypass()
    {
        var tableId = await SeedTableAsync();
        await using var context = fixture.CreateContext();
        var operation = NewOperation(tableId, succeeded: false);
        operation.OutcomeErrorCode = null;
        context.TableReadyOperations.Add(operation);
        var save = () => context.SaveChangesAsync();
        var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        exception.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ck_table_ready_operation_shape");
    }

    private async Task<Guid> SeedTableAsync()
    {
        await using var context = fixture.CreateContext();
        var table = new Table
        {
            TableNumber = "MIG-READY",
            MaxGuests = 4,
            CreatedBy = nameof(TableReadinessMigrationTests)
        };
        context.Tables.Add(table);
        await context.SaveChangesAsync();
        return table.Id;
    }

    private async Task<Guid> SeedOperationAsync()
    {
        var tableId = await SeedTableAsync();
        await using var context = fixture.CreateContext();
        var operation = NewOperation(tableId, succeeded: true);
        context.TableReadyOperations.Add(operation);
        await context.SaveChangesAsync();
        return operation.Id;
    }

    private static TableReadyOperation NewOperation(Guid tableId, bool succeeded) => new()
    {
        TableId = tableId,
        OperationId = Guid.NewGuid(),
        ActorUserId = Guid.NewGuid(),
        ActorRole = UserRole.Server,
        ExpectedReadinessVersion = 1,
        Succeeded = succeeded,
        OutcomeErrorCode = succeeded ? null : "TABLE_VISIT_OPEN",
        OutcomeState = succeeded ? TableReadinessState.ReadyForGuests : TableReadinessState.NeedsReset,
        OutcomeReadinessVersion = succeeded ? 2 : 1,
        RecordedAt = DateTime.UtcNow,
        CreatedBy = nameof(TableReadinessMigrationTests)
    };

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync(target);
    }

    private async Task<string> LatestAppliedMigrationAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1",
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
