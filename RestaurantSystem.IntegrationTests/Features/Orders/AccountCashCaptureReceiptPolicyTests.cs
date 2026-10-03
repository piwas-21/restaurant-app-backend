using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountCashCaptureReceiptPolicyTests
{
    private static readonly DateTime CapturedAt = new(2026, 10, 3, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Cash_capture_freezes_physical_tender_and_returns_only_safe_receipt_fields()
    {
        var fixture = CreateFixture();
        var receipt = AccountCashCaptureReceiptPolicy.Create(fixture.Attempt, fixture.Snapshot,
            fixture.Actor, fixture.Request, fixture.Actor.AuditIdentifier, CapturedAt)!;
        MarkCaptured(fixture.Attempt, receipt, CapturedAt);
        fixture.Attempt.SnapshotJson = AccountPaymentSnapshots.Serialize(fixture.Snapshot);

        AccountCashCaptureReceiptPolicy.ValidateStored(fixture.Attempt, fixture.Snapshot);
        var operation = AccountPaymentSnapshots.ToOperation(fixture.Attempt);

        receipt.ExactAmountMinor.Should().Be(333);
        receipt.AdjustmentMinor.Should().Be(2);
        receipt.DueAmountMinor.Should().Be(335);
        receipt.ReceivedMinor.Should().Be(400);
        receipt.ChangeMinor.Should().Be(65);
        receipt.ExpectedAccountRevision.Should().Be(7);
        receipt.ExpectedVersion.Should().Be(2);
        receipt.ActorId.Should().Be(fixture.Actor.ActorId);
        receipt.RequestHash.Should().HaveLength(64);
        receipt.CapturedAt.Should().Be(fixture.Attempt.CompletedAt);
        operation.CashSettlement.Should().Be(fixture.Snapshot.CashSettlement);
        operation.CashReceipt.Should().Be(new CashCollectionReceiptDto(
            "chf-cash-5-rappen-v1", "CHF", 333, 2, 335, 400, 65, CapturedAt));
        operation.CashReceipt.Should().BeOfType<CashCollectionReceiptDto>();
        typeof(CashCollectionReceiptDto).GetProperty("ActorId").Should().BeNull();
        typeof(CashCollectionReceiptDto).GetProperty("RequestHash").Should().BeNull();
    }

    [Fact]
    public void Cash_capture_replay_accepts_exact_original_request_and_rejects_changed_amount_or_actor()
    {
        var fixture = CapturedFixture();

        var replay = () => AccountCashCaptureReceiptPolicy.RequireReplay(
            fixture.Attempt, fixture.Snapshot, fixture.Actor, fixture.Request);
        var changedReceived = () => AccountCashCaptureReceiptPolicy.RequireReplay(
            fixture.Attempt, fixture.Snapshot, fixture.Actor, fixture.Request with { ReceivedMinor = 500 });
        var changedVersion = () => AccountCashCaptureReceiptPolicy.RequireReplay(
            fixture.Attempt, fixture.Snapshot, fixture.Actor, fixture.Request with { ExpectedVersion = 1 });
        var changedActor = () => AccountCashCaptureReceiptPolicy.RequireReplay(
            fixture.Attempt, fixture.Snapshot, fixture.Actor with { ActorId = Guid.NewGuid() }, fixture.Request);
        var promotedOriginalActor = () => AccountCashCaptureReceiptPolicy.RequireReplay(
            fixture.Attempt, fixture.Snapshot, fixture.Actor with { Role = UserRole.Admin }, fixture.Request);

        replay.Should().NotThrow();
        promotedOriginalActor.Should().NotThrow();
        changedReceived.Should().Throw<ConflictException>();
        changedVersion.Should().Throw<ConflictException>();
        changedActor.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Cash_capture_requires_received_at_least_the_frozen_due_and_a_matching_reservation()
    {
        var fixture = CreateFixture();
        var missingReceived = () => AccountCashCaptureReceiptPolicy.Create(
            fixture.Attempt, fixture.Snapshot, fixture.Actor,
            fixture.Request with { ReceivedMinor = null }, fixture.Actor.AuditIdentifier, CapturedAt);
        var underpayment = () => AccountCashCaptureReceiptPolicy.Create(
            fixture.Attempt, fixture.Snapshot, fixture.Actor,
            fixture.Request with { ReceivedMinor = 334 }, fixture.Actor.AuditIdentifier, CapturedAt);
        var wrongQuoteVersion = () => AccountCashCaptureReceiptPolicy.Create(
            fixture.Attempt, fixture.Snapshot, fixture.Actor,
            fixture.Request with { ExpectedVersion = int.MaxValue }, fixture.Actor.AuditIdentifier, CapturedAt);
        var emptyActor = () => AccountCashCaptureReceiptPolicy.Create(
            fixture.Attempt, fixture.Snapshot, fixture.Actor with { ActorId = Guid.Empty },
            fixture.Request, fixture.Actor.AuditIdentifier, CapturedAt);
        var actorWithoutRole = () => AccountCashCaptureReceiptPolicy.Create(
            fixture.Attempt, fixture.Snapshot, fixture.Actor with { Role = null },
            fixture.Request, fixture.Actor.AuditIdentifier, CapturedAt);
        var guestActor = () => AccountCashCaptureReceiptPolicy.Create(
            fixture.Attempt, fixture.Snapshot, fixture.Actor with { Kind = AccountPaymentActorKind.GuestParticipant },
            fixture.Request, fixture.Actor.AuditIdentifier, CapturedAt);

        missingReceived.Should().Throw<BadRequestException>();
        underpayment.Should().Throw<BadRequestException>().WithMessage("*below the amount due*");
        wrongQuoteVersion.Should().Throw<ConflictException>();
        emptyActor.Should().Throw<ConflictException>();
        actorWithoutRole.Should().Throw<ConflictException>();
        guestActor.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Cash_receipt_is_required_for_new_capture_but_legacy_captured_cash_stays_unattested()
    {
        var fixture = CapturedFixture();
        fixture.Attempt.CashCollectionReceipt = null;
        var snapshotWithoutTerms = fixture.Snapshot with { CashSettlement = null };
        fixture.Attempt.SnapshotJson = AccountPaymentSnapshots.Serialize(snapshotWithoutTerms);

        AccountCashCaptureReceiptPolicy.ValidateStored(fixture.Attempt, snapshotWithoutTerms);
        AccountCashCaptureReceiptPolicy.RequireReplay(fixture.Attempt, snapshotWithoutTerms,
            fixture.Actor, fixture.Request with { ReceivedMinor = null });
        fixture.Attempt.SnapshotJson = """
            {"expectedAccountRevision":7,"mode":"Amount","paymentMethod":"Cash","amountMinor":333,
             "currency":"CHF","quoteExpiresAt":"2026-10-03T10:35:00Z","equalSharePlanId":null,
             "equalShareOrdinal":null,"allocations":[]}
            """;
        AccountPaymentSnapshots.ToOperation(fixture.Attempt).CashReceipt.Should().BeNull();

        var changedRequest = () => AccountCashCaptureReceiptPolicy.RequireReplay(
            fixture.Attempt, snapshotWithoutTerms, fixture.Actor, fixture.Request);
        var mismatchedLegacyVersion = () => AccountCashCaptureReceiptPolicy.RequireReplay(
            fixture.Attempt, snapshotWithoutTerms, fixture.Actor,
            fixture.Request with { ExpectedVersion = fixture.Attempt.Version });
        changedRequest.Should().Throw<ConflictException>();
        mismatchedLegacyVersion.Should().Throw<ConflictException>();

        var missingNewReceipt = CreateFixture();
        missingNewReceipt.Attempt.State = AccountPaymentState.Captured;
        var validate = () => AccountCashCaptureReceiptPolicy.ValidateStored(
            missingNewReceipt.Attempt, missingNewReceipt.Snapshot);
        validate.Should().Throw<ConflictException>().WithMessage("*receipt requires reconciliation*");
    }

    [Fact]
    public void Stored_receipt_requires_captured_state_timestamp_and_untampered_terms()
    {
        var fixture = CapturedFixture();
        var receipt = fixture.Attempt.CashCollectionReceipt!;
        var originalState = fixture.Attempt.State;
        fixture.Attempt.State = AccountPaymentState.Reserved;
        var nonCaptured = () => AccountCashCaptureReceiptPolicy.ValidateStored(fixture.Attempt, fixture.Snapshot);
        nonCaptured.Should().Throw<ConflictException>();
        fixture.Attempt.State = originalState;

        receipt.CapturedAt = CapturedAt.AddSeconds(1);
        var timestampMismatch = () => AccountCashCaptureReceiptPolicy.ValidateStored(
            fixture.Attempt, fixture.Snapshot);
        timestampMismatch.Should().Throw<ConflictException>();
        receipt.CapturedAt = CapturedAt;

        var originalHash = receipt.RequestHash;
        receipt.RequestHash = new string('0', 64);
        var tampered = () => AccountCashCaptureReceiptPolicy.ValidateStored(fixture.Attempt, fixture.Snapshot);
        tampered.Should().Throw<ConflictException>().WithMessage("*request hash*");
        receipt.RequestHash = originalHash;
        receipt.ActorRole = UserRole.Admin;
        var changedOriginalRole = () => AccountCashCaptureReceiptPolicy.ValidateStored(
            fixture.Attempt, fixture.Snapshot);
        changedOriginalRole.Should().Throw<ConflictException>().WithMessage("*request hash*");
        receipt.ActorRole = UserRole.Cashier;
        AccountCashCaptureReceiptPolicy.ValidateStored(fixture.Attempt, fixture.Snapshot);
    }

    [Theory]
    [InlineData(UserRole.Customer)]
    [InlineData((UserRole)999)]
    public void Missing_or_unauthorized_historical_role_fails_closed(UserRole role)
    {
        var fixture = CapturedFixture();
        var receipt = fixture.Attempt.CashCollectionReceipt!;
        receipt.ActorRole = role;

        var validate = () => AccountCashCaptureReceiptPolicy.ValidateStored(fixture.Attempt, fixture.Snapshot);

        validate.Should().Throw<ConflictException>().WithMessage("*requires reconciliation*");
    }

    [Fact]
    public void Private_receipt_schema_accepts_server_role_from_a_resolved_server_policy_context()
    {
        var fixture = CreateFixture();
        var serverActor = fixture.Actor with { Role = UserRole.Server };
        var receipt = AccountCashCaptureReceiptPolicy.Create(fixture.Attempt, fixture.Snapshot,
            serverActor, fixture.Request, serverActor.AuditIdentifier, CapturedAt)!;
        MarkCaptured(fixture.Attempt, receipt, CapturedAt);

        receipt.ActorRole.Should().Be(UserRole.Server);
        AccountCashCaptureReceiptPolicy.ValidateStored(fixture.Attempt, fixture.Snapshot);
        AccountCashCaptureReceiptPolicy.RequireReplay(fixture.Attempt, fixture.Snapshot,
            serverActor with { Role = UserRole.Cashier }, fixture.Request);
    }

    [Fact]
    public void Cash_received_field_is_rejected_for_card_and_card_keeps_exact_terms()
    {
        var fixture = CreateFixture(PaymentMethod.CreditCard);
        AccountCashCaptureReceiptPolicy.Create(fixture.Attempt, fixture.Snapshot,
            fixture.Actor, fixture.Request with { ReceivedMinor = null }, fixture.Actor.AuditIdentifier,
            CapturedAt).Should().BeNull();

        var unexpectedCash = () => AccountCashCaptureReceiptPolicy.Create(fixture.Attempt,
            fixture.Snapshot, fixture.Actor, fixture.Request, fixture.Actor.AuditIdentifier, CapturedAt);
        unexpectedCash.Should().Throw<BadRequestException>().WithMessage("*only valid for a cash contribution*");
        fixture.Snapshot.CashSettlement!.PolicyVersion.Should().Be("exact-v1");
    }

    private static (AccountPaymentAttempt Attempt, AccountPaymentQuoteSnapshot Snapshot,
        AccountPaymentActor Actor, CaptureAccountPaymentRequest Request) CreateFixture(
        PaymentMethod method = PaymentMethod.Cash)
    {
        var actor = new AccountPaymentActor(
            Guid.NewGuid(), AccountPaymentActorKind.Staff, "cashier:test", UserRole.Cashier);
        const long amount = 333;
        const long revision = 7;
        const int version = 2;
        var attempt = new AccountPaymentAttempt
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = Guid.NewGuid(),
            OperationId = Guid.NewGuid(),
            ActorId = actor.ActorId,
            ActorKind = actor.Kind,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Reserved,
            PaymentMethod = method,
            Version = version,
            ExpectedAccountRevision = revision,
            AmountMinor = amount,
            Currency = "CHF",
            PayloadHash = new string('a', 64),
            CreatedAt = CapturedAt.AddMinutes(-1),
            CreatedBy = actor.AuditIdentifier
        };
        var snapshot = new AccountPaymentQuoteSnapshot(
            revision, AccountPaymentMode.Amount, method, amount, "CHF",
            CapturedAt.AddMinutes(5), null, null, [],
            AccountCashSettlementPolicy.Resolve("CHF", method, amount));
        var request = new CaptureAccountPaymentRequest { ExpectedVersion = version, ReceivedMinor = 400 };
        return (attempt, snapshot, actor, request);
    }

    private static (AccountPaymentAttempt Attempt, AccountPaymentQuoteSnapshot Snapshot,
        AccountPaymentActor Actor, CaptureAccountPaymentRequest Request) CapturedFixture()
    {
        var fixture = CreateFixture();
        var receipt = AccountCashCaptureReceiptPolicy.Create(fixture.Attempt, fixture.Snapshot,
            fixture.Actor, fixture.Request, fixture.Actor.AuditIdentifier, CapturedAt)!;
        MarkCaptured(fixture.Attempt, receipt, CapturedAt);
        return fixture;
    }

    private static void MarkCaptured(
        AccountPaymentAttempt attempt, AccountCashCollectionReceipt receipt, DateTime capturedAt)
    {
        attempt.State = AccountPaymentState.Captured;
        attempt.Version++;
        attempt.CompletedAt = capturedAt;
        receipt.CreatedAt = capturedAt;
        receipt.CapturedAt = capturedAt;
        attempt.CashCollectionReceipt = receipt;
    }

}
