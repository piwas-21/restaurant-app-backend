using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class TableSessionLifecycleTenderMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20261009120930_PreserveBundleChoiceSections";
    private const string LifecycleMigration = "20261009131247_AddTableSessionLifecycleAndTenderTips";

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unused_schema_can_rollback_and_upgrade_again()
    {
        try
        {
            await MigrateAsync(PreviousMigration);
            await AssertLifecycleSchemaAsync(present: false);
            await MigrateAsync();
            await AssertLifecycleSchemaAsync(present: true);
        }
        finally
        {
            await MigrateAsync();
        }
    }

    [Fact]
    public async Task Rollback_refuses_released_open_visit_when_next_visit_uses_the_table()
    {
        var tableId = Guid.NewGuid();
        var releasedVisitId = Guid.NewGuid();
        var currentVisitId = Guid.NewGuid();
        var releaseAt = DateTime.UtcNow;
        await using (var seed = fixture.CreateContext())
        {
            seed.Tables.Add(new Table
            {
                Id = tableId,
                TableNumber = "57",
                MaxGuests = 4,
                CreatedAt = releaseAt.AddHours(-1),
                CreatedBy = nameof(TableSessionLifecycleTenderMigrationTests)
            });
            seed.TableServiceSessions.AddRange(
                NewOpenVisit(releasedVisitId, tableId, releasedAt: releaseAt),
                NewOpenVisit(currentVisitId, tableId, releasedAt: null));
            await seed.SaveChangesAsync();
        }

        try
        {
            await AssertRollbackRefusedAsync();
            await using var verify = fixture.CreateContext();
            var visits = await verify.TableServiceSessions.AsNoTracking()
                .Where(value => value.Id == releasedVisitId || value.Id == currentVisitId)
                .OrderBy(value => value.OpenedAt)
                .ToArrayAsync();
            visits.Should().HaveCount(2);
            visits[0].Id.Should().Be(releasedVisitId);
            visits[0].ReleasedAt.Should().Be(releaseAt);
            visits[0].ReleasedBy.Should().Be(nameof(TableSessionLifecycleTenderMigrationTests));
            visits[1].Id.Should().Be(currentVisitId);
            visits[1].ReleasedAt.Should().BeNull();
            visits.Select(value => value.TableId).Should().OnlyContain(value => value == tableId);
        }
        finally
        {
            await MigrateAsync();
        }
    }

    [Theory]
    [InlineData("table-tip")]
    [InlineData("account-tip")]
    [InlineData("custom-plan")]
    [InlineData("custom-attempt")]
    public async Task Rollback_refuses_tender_tip_and_custom_split_history(string evidence)
    {
        var sessionId = await SeedTableAndOpenVisitAsync();
        await using (var seed = fixture.CreateContext())
        {
            switch (evidence)
            {
                case "table-tip":
                    seed.TableBillPaymentOperations.Add(new TableBillPaymentOperation
                    {
                        Id = Guid.NewGuid(),
                        OperationId = Guid.NewGuid(),
                        ServiceSessionId = sessionId,
                        TableNumber = 57,
                        ExpectedVersion = 1,
                        Currency = "CHF",
                        PaymentMethod = PaymentMethod.Cash,
                        Amount = 1m,
                        TipMinor = 50,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = nameof(TableSessionLifecycleTenderMigrationTests)
                    });
                    break;
                case "account-tip":
                    seed.AccountPaymentAttempts.Add(NewAttempt(sessionId, AccountPaymentMode.Amount, tipMinor: 50));
                    break;
                case "custom-plan":
                    seed.AccountEqualSharePlans.Add(NewPlan(sessionId, customAmountsJson: "[50,50]"));
                    break;
                case "custom-attempt":
                    var plan = NewPlan(sessionId, customAmountsJson: null);
                    seed.AccountEqualSharePlans.Add(plan);
                    seed.AccountPaymentAttempts.Add(NewAttempt(
                        sessionId,
                        AccountPaymentMode.CustomAmount,
                        tipMinor: 0,
                        equalSharePlanId: plan.Id,
                        equalShareOrdinal: 1));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(evidence), evidence, "Unknown rollback evidence fixture");
            }

            await seed.SaveChangesAsync();
        }

        await AssertRollbackRefusedAsync();

        await using var verify = fixture.CreateContext();
        switch (evidence)
        {
            case "table-tip":
                (await verify.TableBillPaymentOperations.SingleAsync()).TipMinor.Should().Be(50);
                break;
            case "account-tip":
                (await verify.AccountPaymentAttempts.SingleAsync()).TipMinor.Should().Be(50);
                break;
            case "custom-plan":
                JsonSerializer.Deserialize<long[]>(
                    (await verify.AccountEqualSharePlans.SingleAsync()).CustomAmountsJson!)
                    .Should().Equal(50, 50);
                break;
            case "custom-attempt":
                (await verify.AccountPaymentAttempts.SingleAsync()).Mode.Should().Be(AccountPaymentMode.CustomAmount);
                break;
        }
    }

    private static TableServiceSession NewOpenVisit(Guid id, Guid tableId, DateTime? releasedAt)
    {
        var openedAt = releasedAt?.AddHours(-1) ?? DateTime.UtcNow;
        return new TableServiceSession
        {
            Id = id,
            TableId = tableId,
            TableNumber = 57,
            Status = TableServiceSessionStatus.Open,
            Version = 1,
            AccountRevision = 1,
            BillingAllocationVersion = 1,
            OpenedAt = openedAt,
            ReleasedAt = releasedAt,
            ReleasedBy = releasedAt.HasValue ? nameof(TableSessionLifecycleTenderMigrationTests) : null,
            CreatedAt = openedAt,
            CreatedBy = nameof(TableSessionLifecycleTenderMigrationTests)
        };
    }

    private async Task<Guid> SeedTableAndOpenVisitAsync()
    {
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var openedAt = DateTime.UtcNow;
        await using var seed = fixture.CreateContext();
        seed.Tables.Add(new Table
        {
            Id = tableId,
            TableNumber = "57",
            MaxGuests = 4,
            CreatedAt = openedAt,
            CreatedBy = nameof(TableSessionLifecycleTenderMigrationTests)
        });
        seed.TableServiceSessions.Add(NewOpenVisit(sessionId, tableId, releasedAt: null));
        await seed.SaveChangesAsync();
        return sessionId;
    }

    private static AccountEqualSharePlan NewPlan(Guid sessionId, string? customAmountsJson) => new()
    {
        Id = Guid.NewGuid(),
        ServiceSessionId = sessionId,
        OperationId = Guid.NewGuid(),
        AccountRevision = 1,
        TotalMinor = 100,
        ShareCount = 2,
        Currency = "CHF",
        PayloadHash = new string('a', 64),
        ScopeJson = "[]",
        CustomAmountsJson = customAmountsJson,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = nameof(TableSessionLifecycleTenderMigrationTests)
    };

    private static AccountPaymentAttempt NewAttempt(
        Guid sessionId,
        AccountPaymentMode mode,
        long tipMinor,
        Guid? equalSharePlanId = null,
        int? equalShareOrdinal = null)
    {
        var now = DateTime.UtcNow;
        return new AccountPaymentAttempt
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = mode,
            State = AccountPaymentState.Captured,
            PaymentMethod = PaymentMethod.Cash,
            Version = 1,
            ExpectedAccountRevision = 1,
            AmountMinor = 100,
            TipMinor = tipMinor,
            Currency = "CHF",
            PayloadHash = new string('b', 64),
            SnapshotJson = "{}",
            QuoteExpiresAt = now.AddHours(1),
            CompletedAt = now,
            EqualSharePlanId = equalSharePlanId,
            EqualShareOrdinal = equalShareOrdinal,
            CreatedAt = now,
            CreatedBy = nameof(TableSessionLifecycleTenderMigrationTests)
        };
    }

    private async Task AssertRollbackRefusedAsync()
    {
        var rollback = () => MigrateAsync(PreviousMigration);
        var failure = (await rollback.Should().ThrowAsync<PostgresException>()).Which;
        failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        failure.MessageText.Should().Be(
            "Table session lifecycle, tender-tip, or custom split history must be retained; use a compatible application build and roll forward. Roll back only an unused schema after supported settlement and cleanup.");
        await AssertLifecycleSchemaAsync(present: true);
        await using var verify = fixture.CreateContext();
        (await verify.Database.GetAppliedMigrationsAsync()).Last().Should().Be(LifecycleMigration);
    }

    private async Task AssertLifecycleSchemaAsync(bool present)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT
                EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'table_service_sessions' AND column_name = 'released_at')
                AND EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'table_service_sessions' AND column_name = 'released_by')
                AND EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'table_bill_payment_operations' AND column_name = 'tip_minor')
                AND EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'account_payment_attempts' AND column_name = 'tip_minor')
                AND EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'account_equal_share_plans' AND column_name = 'custom_amounts_json')
                AND EXISTS (SELECT 1 FROM pg_indexes
                    WHERE schemaname = 'public' AND tablename = 'table_service_sessions'
                      AND indexname = 'IX_table_service_sessions_table_number'
                      AND indexdef LIKE '%released_at%')
            """, connection);
        ((bool)(await command.ExecuteScalarAsync())!).Should().Be(present,
            present
                ? "a refused rollback must leave lifecycle, tip and split schema intact"
                : "an unused schema rollback must remove the lifecycle, tip and split additions");
    }

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync(target);
    }
}
