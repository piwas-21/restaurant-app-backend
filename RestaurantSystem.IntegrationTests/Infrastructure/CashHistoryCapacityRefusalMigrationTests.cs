using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class CashHistoryCapacityRefusalMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20261004043011_AddAccountCashRefundEvidence"; // pragma: allowlist secret
    private const string LatestMigration = "20261004105734_AddCashHistoryCapacityRefusal"; // pragma: allowlist secret
    private static readonly string[] ExistingFailureCodes =
    [
        "quoteExpired",
        "sourceVersionConflict",
        "accountRevisionConflict",
        "quoteChanged"
    ];

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_rollback_restores_old_check_and_upgrade_registers_new_reason()
    {
        var seeded = await SeedParentsAsync();
        try
        {
            await MigrateAsync(PreviousMigration);
            (await LatestAppliedMigrationAsync()).Should().Be(PreviousMigration);

            var oldSchemaFailure = await InsertRefusalFailureAsync(seeded, "cashHistoryCapacityExceeded");
            oldSchemaFailure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            oldSchemaFailure.ConstraintName.Should().Be("ck_amendment_resolution_refusal_code");
            (await RefusalCountAsync()).Should().Be(0);

            await MigrateAsync();
            (await LatestAppliedMigrationAsync()).Should().Be(LatestMigration);
            await using (var context = fixture.CreateContext())
                context.Database.GetMigrations().Should().Contain(LatestMigration);

            await InsertRefusalAsync(seeded, "cashHistoryCapacityExceeded");
            await using var verify = fixture.CreateContext();
            (await verify.OrderAmendmentResolutionRefusals.AsNoTracking()
                    .SingleAsync(value => value.FailureCode == "cashHistoryCapacityExceeded"))
                .ClientOperationId.Should().Be(seeded.ClientOperationId);
        }
        finally
        {
            await MigrateAsync();
        }
    }

    [Fact]
    public async Task Used_capacity_refusal_blocks_rollback_and_retains_row_and_parents()
    {
        var seeded = await SeedParentsAsync();
        await InsertRefusalAsync(seeded, "cashHistoryCapacityExceeded");
        try
        {
            var rollback = () => MigrateAsync(PreviousMigration);
            var refusal = (await rollback.Should().ThrowAsync<PostgresException>()).Which;
            refusal.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            refusal.MessageText.Should().Be("Cash history capacity refusal history must be retained");

            (await LatestAppliedMigrationAsync()).Should().Be(LatestMigration);
            (await RefusalCountAsync()).Should().Be(1);
            await using var verify = fixture.CreateContext();
            (await verify.Orders.CountAsync(value => value.Id == seeded.OrderId)).Should().Be(1);
            (await verify.OrderAmendments.CountAsync(value => value.Id == seeded.AmendmentId)).Should().Be(1);
            (await verify.OrderAmendmentResolutionRefusals.AsNoTracking()
                    .SingleAsync(value => value.ClientOperationId == seeded.ClientOperationId))
                .FailureCode.Should().Be("cashHistoryCapacityExceeded");
        }
        finally
        {
            await MigrateAsync();
        }
    }

    [Fact]
    public async Task Existing_refusal_codes_remain_valid_and_unknown_code_is_rejected_by_database()
    {
        var seeded = await SeedParentsAsync();
        foreach (var failureCode in ExistingFailureCodes)
            await InsertRefusalAsync(seeded with { ClientOperationId = Guid.NewGuid() }, failureCode);

        (await RefusalCountAsync()).Should().Be(ExistingFailureCodes.Length);
        var unknown = await InsertRefusalFailureAsync(seeded with { ClientOperationId = Guid.NewGuid() }, "unknownReason");
        unknown.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        unknown.ConstraintName.Should().Be("ck_amendment_resolution_refusal_code");
        (await RefusalCountAsync()).Should().Be(ExistingFailureCodes.Length);
    }

    private async Task<SeededParents> SeedParentsAsync()
    {
        var now = DateTime.UtcNow;
        var actorId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"CAP-{Guid.NewGuid():N}"[..20],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            Version = 1,
            OrderDate = now,
            CreatedBy = nameof(CashHistoryCapacityRefusalMigrationTests)
        };
        var amendment = new OrderAmendment
        {
            Id = Guid.NewGuid(),
            SourceOrderId = order.Id,
            ActorUserId = actorId,
            ActorRole = "Admin",
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('a', 64),
            CommitPayloadHash = new string('b', 64),
            ExpectedOrderVersion = 1,
            ExpiresAt = now.AddMinutes(1),
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = "[]",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            CreatedBy = nameof(CashHistoryCapacityRefusalMigrationTests)
        };

        await using var context = fixture.CreateContext();
        context.Orders.Add(order);
        context.OrderAmendments.Add(amendment);
        await context.SaveChangesAsync();
        return new SeededParents(order.Id, amendment.Id, actorId, Guid.NewGuid());
    }

    private async Task InsertRefusalAsync(SeededParents seeded, string failureCode)
    {
        await using var context = fixture.CreateContext();
        context.OrderAmendmentResolutionRefusals.Add(new OrderAmendmentResolutionRefusal
        {
            Id = Guid.NewGuid(),
            ActorUserId = seeded.ActorId,
            SourceOrderId = seeded.OrderId,
            AmendmentId = seeded.AmendmentId,
            ClientOperationId = seeded.ClientOperationId,
            RequestHash = new string('c', 64),
            FailureCode = failureCode,
            OriginalRequestJson = "{}",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CashHistoryCapacityRefusalMigrationTests)
        });
        await context.SaveChangesAsync();
    }

    private async Task<PostgresException> InsertRefusalFailureAsync(SeededParents seeded, string failureCode)
    {
        var insert = () => InsertRefusalAsync(seeded, failureCode);
        var failure = (await insert.Should().ThrowAsync<DbUpdateException>()).Which;
        return failure.InnerException.Should().BeOfType<PostgresException>().Which;
    }

    private async Task<int> RefusalCountAsync()
    {
        await using var context = fixture.CreateContext();
        return await context.OrderAmendmentResolutionRefusals.CountAsync();
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

    private sealed record SeededParents(Guid OrderId, Guid AmendmentId, Guid ActorId, Guid ClientOperationId);
}
