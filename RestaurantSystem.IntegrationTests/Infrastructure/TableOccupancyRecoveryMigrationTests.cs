using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class TableOccupancyRecoveryMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PriorMigration = "20261009140008_AddOrderPaymentTips";
    private const string LatestMigration = TestDatabaseCluster.CurrentSchemaMigration;

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Upgrade_backfills_existing_share_plans_and_preserves_their_reviewed_scope()
    {
        var sessionId = await SeedClosedSessionAsync();
        var oldPlanId = Guid.NewGuid();
        var oldOperationId = Guid.NewGuid();
        try
        {
            await MigrateAsync(PriorMigration);
            await InsertPlanWithoutRoundingColumnAsync(sessionId, oldPlanId, oldOperationId);
            await MigrateAsync();

            var oldPlan = await ReadPlanAsync(oldPlanId);
            oldPlan.RoundingIncrement.Should().Be(1);
            oldPlan.TotalMinor.Should().Be(235);
            oldPlan.ShareCount.Should().Be(3);
            oldPlan.ScopeJson.Should().Be("[]");

            var futurePlanId = Guid.NewGuid();
            await InsertPlanWithDatabaseDefaultAsync(sessionId, futurePlanId, Guid.NewGuid());
            (await ReadPlanAsync(futurePlanId)).RoundingIncrement.Should().Be(1);
        }
        finally
        {
            await MigrateAsync();
        }
    }

    [Fact]
    public async Task Rollback_refuses_to_drop_a_five_minor_unit_share_plan()
    {
        var sessionId = await SeedClosedSessionAsync();
        await using (var context = fixture.CreateContext())
        {
            context.AccountEqualSharePlans.Add(new AccountEqualSharePlan
            {
                Id = Guid.NewGuid(),
                ServiceSessionId = sessionId,
                OperationId = Guid.NewGuid(),
                AccountRevision = 1,
                TotalMinor = 460,
                ShareCount = 3,
                RoundingIncrementMinor = 5,
                Currency = "CHF",
                PayloadHash = new string('a', 64),
                ScopeJson = "[]",
                CreatedBy = nameof(TableOccupancyRecoveryMigrationTests)
            });
            await context.SaveChangesAsync();
        }

        var rollback = () => MigrateAsync(PriorMigration);
        (await rollback.Should().ThrowAsync<PostgresException>()).Which.SqlState
            .Should().Be(PostgresErrorCodes.CheckViolation);
        (await LatestAppliedMigrationAsync()).Should().Be(LatestMigration);
    }

    private async Task<Guid> SeedClosedSessionAsync()
    {
        var now = DateTime.UtcNow;
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 1,
            Currency = "CHF",
            Status = TableServiceSessionStatus.Closed,
            Version = 1,
            AccountRevision = 1,
            OpenedAt = now.AddMinutes(-1),
            ClosedAt = now,
            CreatedBy = nameof(TableOccupancyRecoveryMigrationTests)
        };
        await using var context = fixture.CreateContext();
        context.TableServiceSessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
    }

    private async Task InsertPlanWithoutRoundingColumnAsync(Guid sessionId, Guid planId, Guid operationId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO account_equal_share_plans
              (id, service_session_id, operation_id, account_revision, total_minor, share_count,
               currency, payload_hash, scope_json, created_at, created_by)
            VALUES (@id, @session, @operation, 1, 235, 3, 'CHF', repeat('a', 64), '[]'::jsonb,
                    @created, 'legacy-share-plan')
            """, connection);
        command.Parameters.AddWithValue("id", planId);
        command.Parameters.AddWithValue("session", sessionId);
        command.Parameters.AddWithValue("operation", operationId);
        command.Parameters.AddWithValue("created", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertPlanWithDatabaseDefaultAsync(Guid sessionId, Guid planId, Guid operationId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO account_equal_share_plans
              (id, service_session_id, operation_id, account_revision, total_minor, share_count,
               currency, payload_hash, scope_json, created_at, created_by)
            VALUES (@id, @session, @operation, 1, 235, 3, 'CHF', repeat('b', 64), '[]'::jsonb,
                    @created, 'default-share-plan')
            """, connection);
        command.Parameters.AddWithValue("id", planId);
        command.Parameters.AddWithValue("session", sessionId);
        command.Parameters.AddWithValue("operation", operationId);
        command.Parameters.AddWithValue("created", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SharePlanRow> ReadPlanAsync(Guid planId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT rounding_increment_minor, total_minor, share_count, scope_json::text
            FROM account_equal_share_plans WHERE id = @id
            """, connection);
        command.Parameters.AddWithValue("id", planId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new SharePlanRow(reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetString(3));
    }

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

    private sealed record SharePlanRow(int RoundingIncrement, long TotalMinor, int ShareCount, string ScopeJson);
}
