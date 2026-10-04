using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class AccountCashCollectionReceiptMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string BeforeCashReceipts = "20261003153304_AddOrderAmendmentResolutionRefusals";
    private const string CashMigration = "20261004031747_AddAccountCashCollectionReceipts";
    private const string RefundMigration = "20261004043011_AddAccountCashRefundEvidence";
    private static readonly DateTime CapturedAt = new(2026, 10, 4, 3, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_rollback_and_upgrade_restore_the_receipt_table_and_migration()
    {
        try
        {
            await MigrateAsync(BeforeCashReceipts);
            (await ScalarAsync("SELECT to_regclass('account_cash_collection_receipts') IS NULL"))
                .Should().Be(true);
            (await ScalarAsync("SELECT to_regclass('account_cash_refund_intents') IS NULL"))
                .Should().Be(true);
            (await ScalarAsync("SELECT to_regclass('account_cash_refund_evidence') IS NULL"))
                .Should().Be(true);
            await MigrateAsync();
            (await ScalarAsync("SELECT to_regclass('account_cash_collection_receipts') IS NOT NULL"))
                .Should().Be(true);
            (await ScalarAsync("SELECT to_regclass('account_cash_refund_intents') IS NOT NULL"))
                .Should().Be(true);
            (await ScalarAsync("SELECT to_regclass('account_cash_refund_evidence') IS NOT NULL"))
                .Should().Be(true);
            (await ScalarAsync("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @value", CashMigration))
                .Should().Be(1L);
            (await ScalarAsync("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @value", RefundMigration))
                .Should().Be(1L);
        }
        finally
        {
            await MigrateAsync();
        }
    }

    [Fact]
    public async Task Stored_physical_receipt_is_immutable_and_prevents_financial_history_rollback()
    {
        var receipt = await SeedAsync();
        var update = () => ScalarAsync(
            "UPDATE account_cash_collection_receipts SET received_minor = 500, change_minor = 165 WHERE id = @value RETURNING id",
            receipt.Id);
        var delete = () => ScalarAsync(
            "DELETE FROM account_cash_collection_receipts WHERE id = @value RETURNING id", receipt.Id);
        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
        var rollback = () => MigrateAsync(BeforeCashReceipts);
        (await rollback.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");

        await using var context = fixture.CreateContext();
        var saved = await context.Set<AccountCashCollectionReceipt>().AsNoTracking().SingleAsync();
        saved.Id.Should().Be(receipt.Id);
        saved.ExactAmountMinor.Should().Be(333);
        saved.AdjustmentMinor.Should().Be(2);
        saved.DueAmountMinor.Should().Be(335);
        saved.ReceivedMinor.Should().Be(400);
        saved.ChangeMinor.Should().Be(65);
        saved.ActorRole.Should().Be(UserRole.Server);
        saved.CapturedAt.Should().Be(CapturedAt);
        (await ScalarAsync("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @value", CashMigration))
            .Should().Be(1L);

        await MigrateAsync();
        (await ScalarAsync("SELECT count(*) FROM account_cash_refund_intents")).Should().Be(0L);
        (await ScalarAsync("SELECT count(*) FROM account_cash_refund_evidence")).Should().Be(0L);
        await fixture.ResetDatabaseAsync();
        (await ScalarAsync("SELECT count(*) FROM account_cash_collection_receipts")).Should().Be(0L,
            "disposable lane cleanup must clear immutable receipts without disabling their production triggers");
    }

    [Theory]
    [InlineData("received")]
    [InlineData("change")]
    [InlineData("adjustment")]
    [InlineData("role")]
    [InlineData("policy")]
    [InlineData("hash")]
    public async Task Database_rejects_unconserved_or_unauthorized_receipt_shape(string corruption)
    {
        var original = await SeedAsync();
        await using var context = fixture.CreateContext();
        var attempt = NewAttempt(original.Attempt!.ServiceSessionId);
        var hostile = NewReceipt(attempt);
        switch (corruption)
        {
            case "received": hostile.ReceivedMinor = 334; hostile.ChangeMinor = 0; break;
            case "change": hostile.ChangeMinor = 64; break;
            case "adjustment": hostile.AdjustmentMinor = 1; break;
            case "role": hostile.ActorRole = UserRole.Customer; break;
            case "policy": hostile.PolicyVersion = "exact-v1"; break;
            case "hash": hostile.RequestHash = new string('z', 64); break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        context.AccountPaymentAttempts.Add(attempt);
        context.Set<AccountCashCollectionReceipt>().Add(hostile);
        var write = () => context.SaveChangesAsync();
        var failure = (await write.Should().ThrowAsync<DbUpdateException>()).Which;
        failure.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be("23514");
        (await ScalarAsync("SELECT count(*) FROM account_cash_collection_receipts")).Should().Be(1L);
    }

    [Theory]
    [InlineData(false, "23505")]
    [InlineData(true, "23503")]
    public async Task Receipt_requires_one_existing_original_attempt(bool missingAttempt, string sqlState)
    {
        var original = await SeedAsync();
        await using var context = fixture.CreateContext();
        var duplicate = NewReceipt(NewAttempt(original.Attempt!.ServiceSessionId));
        duplicate.Attempt = null;
        duplicate.AttemptId = missingAttempt ? Guid.NewGuid() : original.AttemptId;
        context.Set<AccountCashCollectionReceipt>().Add(duplicate);
        var write = () => context.SaveChangesAsync();
        var failure = (await write.Should().ThrowAsync<DbUpdateException>()).Which;
        failure.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(sqlState);
        (await ScalarAsync("SELECT count(*) FROM account_cash_collection_receipts")).Should().Be(1L);
    }

    private async Task<AccountCashCollectionReceipt> SeedAsync()
    {
        await using var context = fixture.CreateContext();
        var visit = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 17,
            Currency = "CHF",
            OpenedAt = CapturedAt,
            CreatedBy = "cash-migration-test"
        };
        var attempt = NewAttempt(visit.Id);
        var receipt = NewReceipt(attempt);
        context.TableServiceSessions.Add(visit);
        context.AccountPaymentAttempts.Add(attempt);
        context.Set<AccountCashCollectionReceipt>().Add(receipt);
        await context.SaveChangesAsync();
        return receipt;
    }

    private static AccountPaymentAttempt NewAttempt(Guid visitId) => new()
    {
        Id = Guid.NewGuid(),
        ServiceSessionId = visitId,
        OperationId = Guid.NewGuid(),
        ActorId = Guid.NewGuid(),
        ActorKind = AccountPaymentActorKind.Staff,
        Mode = AccountPaymentMode.Amount,
        State = AccountPaymentState.Captured,
        PaymentMethod = PaymentMethod.Cash,
        Version = 3,
        ExpectedAccountRevision = 1,
        AmountMinor = 333,
        Currency = "CHF",
        PayloadHash = new string('a', 64),
        SnapshotJson = "{}",
        QuoteExpiresAt = CapturedAt.AddMinutes(5),
        CompletedAt = CapturedAt,
        CreatedBy = "cash-migration-test"
    };

    private static AccountCashCollectionReceipt NewReceipt(AccountPaymentAttempt attempt) => new()
    {
        Id = Guid.NewGuid(),
        AttemptId = attempt.Id,
        Attempt = attempt,
        PolicyVersion = "chf-cash-5-rappen-v1",
        Currency = "CHF",
        PaymentMethod = PaymentMethod.Cash,
        ExactAmountMinor = 333,
        AdjustmentMinor = 2,
        DueAmountMinor = 335,
        ReceivedMinor = 400,
        ChangeMinor = 65,
        ExpectedAccountRevision = 1,
        ExpectedVersion = 2,
        RequestHash = new string('a', 64),
        ActorId = attempt.ActorId,
        ActorKind = AccountPaymentActorKind.Staff,
        ActorRole = UserRole.Server,
        CapturedAt = CapturedAt,
        CreatedAt = CapturedAt,
        CreatedBy = "cash-migration-test"
    };

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync(target);
    }

    private async Task<object?> ScalarAsync(string sql, object? value = null)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (value is not null) command.Parameters.AddWithValue("value", value);
        return await command.ExecuteScalarAsync();
    }
}
