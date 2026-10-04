using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class AccountCashRefundEvidenceMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string CashMigration = "20261004031747_AddAccountCashCollectionReceipts";
    private const string RefundMigration = "20261004043011_AddAccountCashRefundEvidence";
    private static readonly DateTime ObservedAt = new(2026, 10, 4, 4, 30, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_refund_history_rollback_and_upgrade_preserve_the_receipt_schema()
    {
        try
        {
            await MigrateAsync(CashMigration);
            (await ScalarAsync("SELECT to_regclass('account_cash_refund_intents') IS NULL"))
                .Should().Be(true);
            (await ScalarAsync("SELECT to_regclass('account_cash_refund_evidence') IS NULL"))
                .Should().Be(true);
            (await ScalarAsync("SELECT to_regclass('account_cash_collection_receipts') IS NOT NULL"))
                .Should().Be(true);

            await MigrateAsync();

            (await ScalarAsync("SELECT to_regclass('account_cash_refund_intents') IS NOT NULL"))
                .Should().Be(true);
            (await ScalarAsync("SELECT to_regclass('account_cash_refund_evidence') IS NOT NULL"))
                .Should().Be(true);
            (await ScalarAsync("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @value",
                RefundMigration)).Should().Be(1L);
        }
        finally
        {
            await MigrateAsync();
        }
    }

    [Fact]
    public async Task Refund_intent_and_return_are_immutable_and_block_nonempty_rollback()
    {
        var seeded = await SeedAsync();
        try
        {
            var intentUpdate = () => ExecuteNonQueryAsync(
                "UPDATE account_cash_refund_intents SET created_by = 'tampered' WHERE id = $1",
                seeded.IntentId);
            var intentDelete = () => ExecuteNonQueryAsync(
                "DELETE FROM account_cash_refund_intents WHERE id = $1", seeded.IntentId);
            var evidenceUpdate = () => ExecuteNonQueryAsync(
                "UPDATE account_cash_refund_evidence SET till_reference = 'changed-ref' WHERE id = $1",
                seeded.EvidenceId);
            var evidenceDelete = () => ExecuteNonQueryAsync(
                "DELETE FROM account_cash_refund_evidence WHERE id = $1", seeded.EvidenceId);

            (await intentUpdate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
            (await intentDelete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
            (await evidenceUpdate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
            (await evidenceDelete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");

            var rollback = () => MigrateAsync(CashMigration);
            (await rollback.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
            (await ScalarAsync("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @value",
                RefundMigration)).Should().Be(1L);
            (await ScalarAsync("SELECT count(*) FROM account_cash_refund_intents")).Should().Be(1L);
            (await ScalarAsync("SELECT count(*) FROM account_cash_refund_evidence")).Should().Be(1L);
        }
        finally
        {
            await MigrateAsync();
            await fixture.ResetDatabaseAsync();
        }

        (await ScalarAsync("SELECT count(*) FROM account_cash_refund_intents")).Should().Be(0L,
            "the disposable lane reset may clear history only after the latest migration is restored");
        (await ScalarAsync("SELECT count(*) FROM account_cash_refund_evidence")).Should().Be(0L);
    }

    [Fact]
    public async Task Database_rejects_invalid_shapes_missing_foreign_keys_and_duplicate_owners()
    {
        var seeded = await SeedAsync();
        var invalidIntent = () => ExecuteNonQueryAsync("""
            INSERT INTO account_cash_refund_intents (
                id, refund_leg_id, operation_id, attempt_id, collection_receipt_id,
                policy_version, currency, original_exact_amount_minor, original_adjustment_minor,
                original_due_amount_minor, previously_refunded_exact_minor,
                previously_refunded_cash_minor, exact_refund_amount_minor,
                refund_adjustment_minor, cash_refund_amount_minor,
                retained_exact_amount_minor, retained_cash_due_minor,
                prior_history_fingerprint, created_by)
            VALUES ($1, $2, $3, $4, $5, 'chf-cash-5-rappen-v1', 'EUR',
                333, 2, 335, 0, 0, 333, 2, 335, 0, 0, repeat('c', 64), 'migration-test')
            """, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        (await invalidIntent.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");

        var missingIntentForeignKey = () => ExecuteNonQueryAsync("""
            INSERT INTO account_cash_refund_intents (
                id, refund_leg_id, operation_id, attempt_id, collection_receipt_id,
                policy_version, currency, original_exact_amount_minor, original_adjustment_minor,
                original_due_amount_minor, previously_refunded_exact_minor,
                previously_refunded_cash_minor, exact_refund_amount_minor,
                refund_adjustment_minor, cash_refund_amount_minor,
                retained_exact_amount_minor, retained_cash_due_minor,
                prior_history_fingerprint, created_by)
            VALUES ($1, $2, $3, $4, $5, 'chf-cash-5-rappen-v1', 'CHF',
                333, 2, 335, 0, 0, 333, 2, 335, 0, 0, repeat('c', 64), 'migration-test')
            """, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        (await missingIntentForeignKey.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23503");

        var invalidEvidence = () => ExecuteNonQueryAsync("""
            INSERT INTO account_cash_refund_evidence (
                id, intent_id, exact_refund_amount_minor, refund_adjustment_minor,
                cash_returned_minor, currency, actor_id, actor_role, till_reference,
                observed_at, created_by)
            VALUES ($1, $2, 333, 2, 335, 'CHF', $3, 'Server', 'till-ref', $4, 'migration-test')
            """, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ObservedAt);
        (await invalidEvidence.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");

        var missingEvidenceForeignKey = () => ExecuteNonQueryAsync("""
            INSERT INTO account_cash_refund_evidence (
                id, intent_id, exact_refund_amount_minor, refund_adjustment_minor,
                cash_returned_minor, currency, actor_id, actor_role, till_reference,
                observed_at, created_by)
            VALUES ($1, $2, 333, 2, 335, 'CHF', $3, 'Admin', 'till-ref', $4, 'migration-test')
            """, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ObservedAt);
        (await missingEvidenceForeignKey.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23503");

        var duplicateIntent = () => ExecuteNonQueryAsync("""
            INSERT INTO account_cash_refund_intents (
                id, refund_leg_id, operation_id, attempt_id, collection_receipt_id,
                policy_version, currency, original_exact_amount_minor, original_adjustment_minor,
                original_due_amount_minor, previously_refunded_exact_minor,
                previously_refunded_cash_minor, exact_refund_amount_minor,
                refund_adjustment_minor, cash_refund_amount_minor,
                retained_exact_amount_minor, retained_cash_due_minor,
                prior_history_fingerprint, created_by)
            SELECT $1, refund_leg_id, operation_id, attempt_id, collection_receipt_id,
                policy_version, currency, original_exact_amount_minor, original_adjustment_minor,
                original_due_amount_minor, previously_refunded_exact_minor,
                previously_refunded_cash_minor, exact_refund_amount_minor,
                refund_adjustment_minor, cash_refund_amount_minor,
                retained_exact_amount_minor, retained_cash_due_minor,
                prior_history_fingerprint, 'migration-test'
            FROM account_cash_refund_intents WHERE id = $2
            """, Guid.NewGuid(), seeded.IntentId);
        (await duplicateIntent.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");

        var duplicateEvidence = () => ExecuteNonQueryAsync("""
            INSERT INTO account_cash_refund_evidence (
                id, intent_id, exact_refund_amount_minor, refund_adjustment_minor,
                cash_returned_minor, currency, actor_id, actor_role, till_reference,
                observed_at, created_by)
            SELECT $1, intent_id, exact_refund_amount_minor, refund_adjustment_minor,
                cash_returned_minor, currency, actor_id, actor_role, till_reference,
                observed_at, 'migration-test'
            FROM account_cash_refund_evidence WHERE id = $2
            """, Guid.NewGuid(), seeded.EvidenceId);
        (await duplicateEvidence.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");

        (await ScalarAsync("SELECT count(*) FROM account_cash_refund_intents")).Should().Be(1L);
        (await ScalarAsync("SELECT count(*) FROM account_cash_refund_evidence")).Should().Be(1L);
    }

    private async Task<RefundMigrationSeed> SeedAsync()
    {
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var amendmentId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var legId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();
        var actorId = Guid.Parse(TestAuthHandler.AdminUserId);
        var now = ObservedAt;
        var session = new TableServiceSession
        {
            Id = sessionId,
            TableNumber = 17,
            Currency = "CHF",
            OpenedAt = now,
            CreatedBy = "cash-refund-migration-test"
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"CR-{orderId:N}"[..15],
            ServiceSessionId = sessionId,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            Version = 1,
            SubTotal = 3.33m,
            Total = 3.33m,
            TotalPaid = 3.33m,
            RemainingAmount = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = "cash-refund-migration-test"
        };
        var payment = new OrderPayment
        {
            Id = paymentId,
            OrderId = orderId,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 3.33m,
            Currency = "CHF",
            Status = PaymentStatus.Completed,
            PaymentDate = now,
            CreatedAt = now,
            CreatedBy = "cash-refund-migration-test"
        };
        var amendment = new OrderAmendment
        {
            Id = amendmentId,
            SourceOrderId = orderId,
            ServiceSessionId = sessionId,
            ActorUserId = actorId,
            ActorRole = UserRole.Admin.ToString(),
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('a', 64),
            CommitPayloadHash = new string('b', 64),
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            CommittedAccountRevision = 1,
            ExpiresAt = now.AddHours(1),
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = "[]",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            CreatedAt = now,
            CreatedBy = "cash-refund-migration-test"
        };
        var operation = new OrderAmendmentResolutionOperation
        {
            Id = operationId,
            AmendmentId = amendmentId,
            SourceOrderId = orderId,
            ServiceSessionId = sessionId,
            ClientOperationId = Guid.NewGuid(),
            ActorUserId = actorId,
            ActorRole = UserRole.Admin.ToString(),
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            Currency = "CHF",
            CreditMinor = 333,
            RefundMinor = 333,
            UnpaidWaivedMinor = 0,
            RequestHash = new string('c', 64),
            SnapshotJson = "{}",
            State = OrderAmendmentResolutionOperationState.Processing,
            StartedAt = now,
            CreatedAt = now,
            CreatedBy = "cash-refund-migration-test"
        };
        var attempt = new AccountPaymentAttempt
        {
            Id = attemptId,
            ServiceSessionId = sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Captured,
            PaymentMethod = PaymentMethod.Cash,
            Version = 2,
            ExpectedAccountRevision = 1,
            AmountMinor = 333,
            Currency = "CHF",
            PayloadHash = new string('d', 64),
            SnapshotJson = "{}",
            QuoteExpiresAt = now.AddHours(1),
            CompletedAt = now,
            CreatedAt = now,
            CreatedBy = "cash-refund-migration-test"
        };
        var receipt = new AccountCashCollectionReceipt
        {
            Id = receiptId,
            AttemptId = attemptId,
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
            RequestHash = new string('e', 64),
            ActorId = attempt.ActorId,
            ActorKind = AccountPaymentActorKind.Staff,
            ActorRole = UserRole.Cashier,
            CapturedAt = now,
            CreatedAt = now,
            CreatedBy = "cash-refund-migration-test"
        };
        var leg = new OrderAmendmentRefundLeg
        {
            Id = legId,
            OperationId = operationId,
            SourcePaymentId = paymentId,
            AccountPaymentAttemptId = attemptId,
            Custody = OrderAmendmentRefundCustody.ManualTill,
            State = OrderAmendmentRefundLegState.Succeeded,
            AmountMinor = 333,
            Currency = "CHF",
            FrozenScopesJson = "[]",
            ManualTillReference = "migration-refund",
            CreatedAt = now,
            CreatedBy = "cash-refund-migration-test"
        };
        var intent = new AccountCashRefundIntent
        {
            Id = intentId,
            RefundLegId = legId,
            OperationId = operationId,
            AttemptId = attemptId,
            CollectionReceiptId = receiptId,
            PolicyVersion = "chf-cash-5-rappen-v1",
            Currency = "CHF",
            OriginalExactAmountMinor = 333,
            OriginalAdjustmentMinor = 2,
            OriginalDueAmountMinor = 335,
            PreviouslyRefundedExactMinor = 0,
            PreviouslyRefundedCashMinor = 0,
            ExactRefundAmountMinor = 333,
            RefundAdjustmentMinor = 2,
            CashRefundAmountMinor = 335,
            RetainedExactAmountMinor = 0,
            RetainedCashDueMinor = 0,
            PriorHistoryFingerprint = new string('f', 64),
            CreatedAt = now,
            CreatedBy = "cash-refund-migration-test"
        };
        var evidence = new AccountCashRefundEvidence
        {
            Id = evidenceId,
            IntentId = intentId,
            ExactRefundAmountMinor = 333,
            RefundAdjustmentMinor = 2,
            CashReturnedMinor = 335,
            Currency = "CHF",
            ActorId = actorId,
            ActorRole = UserRole.Admin,
            TillReference = "migration-refund",
            ObservedAt = now,
            CreatedAt = now,
            CreatedBy = "cash-refund-migration-test"
        };

        await using var context = fixture.CreateContext();
        order.Payments = [payment];
        operation.Legs = [leg];
        leg.CashRefundIntent = intent;
        intent.ReturnEvidence = evidence;
        context.TableServiceSessions.Add(session);
        context.Orders.Add(order);
        context.OrderAmendments.Add(amendment);
        context.OrderAmendmentResolutionOperations.Add(operation);
        context.AccountPaymentAttempts.Add(attempt);
        context.AccountCashCollectionReceipts.Add(receipt);
        context.AccountCashRefundIntents.Add(intent);
        context.AccountCashRefundEvidence.Add(evidence);
        await context.SaveChangesAsync();

        return new RefundMigrationSeed(receiptId, intentId, evidenceId);
    }

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
        if (value is not null)
            command.Parameters.AddWithValue("value", value);
        return await command.ExecuteScalarAsync();
    }

    private async Task<int> ExecuteNonQueryAsync(string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        return await command.ExecuteNonQueryAsync();
    }

    private sealed record RefundMigrationSeed(Guid ReceiptId, Guid IntentId, Guid EvidenceId);
}
