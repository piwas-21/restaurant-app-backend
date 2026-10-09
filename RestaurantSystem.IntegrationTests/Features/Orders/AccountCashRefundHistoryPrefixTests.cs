using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountCashRefundHistoryPrefixTests
{
    private const string SwissCashPolicy = "chf-cash-5-rappen-v1";
    private const string FingerprintBeforePendingLeg = "cash-history-before-pending-leg";
    private const string Creator = "prefix-test";

    [Fact]
    public void Pending_tail_is_accepted_after_the_target_order_history_prefix_is_fully_validated()
    {
        var fixture = CreateFixture();

        Action action = () => RequireValid(fixture);
        action.Should().NotThrow();
    }

    [Fact]
    public void Pending_tail_cannot_reuse_a_target_source_order()
    {
        var fixture = CreateFixture();
        fixture.Operation.SourceOrderId = fixture.TargetSourceOrderId;

        RequireRejected(fixture);
    }

    [Fact]
    public void Pending_tail_must_belong_to_the_attempt_service_session()
    {
        var fixture = CreateFixture();
        fixture.Operation.ServiceSessionId = Id(31);

        RequireRejected(fixture);
    }

    [Fact]
    public void Pending_tail_scope_must_belong_to_its_source_order()
    {
        var fixture = CreateFixture();
        fixture.Leg.FrozenScopesJson = SerializeScope(CreateScope(
            fixture.AllocationId, fixture.TargetSourceOrderId, Id(15),
            startOrdinal: 1, unitCount: 1, amountMinor: 250));

        RequireRejected(fixture);
    }

    [Fact]
    public void Pending_tail_scope_must_match_the_allocation_source_order()
    {
        var fixture = CreateFixture();
        fixture.Allocations[fixture.AllocationId].OrderId = fixture.TargetSourceOrderId;

        RequireRejected(fixture);
    }

    [Theory]
    [InlineData("original-exact")]
    [InlineData("original-due")]
    [InlineData("previous-exact")]
    [InlineData("previous-cash")]
    [InlineData("refund-exact")]
    [InlineData("refund-adjustment")]
    [InlineData("refund-cash")]
    [InlineData("retained-exact")]
    [InlineData("retained-cash")]
    [InlineData("scope-total")]
    public void Pending_tail_requires_independently_frozen_exact_and_physical_amounts(string mutation)
    {
        var fixture = CreateFixture();

        switch (mutation)
        {
            case "original-exact":
                fixture.Intent.OriginalExactAmountMinor = 1_001;
                break;
            case "original-due":
                fixture.Intent.OriginalDueAmountMinor = 1_005;
                break;
            case "previous-exact":
                fixture.Intent.PreviouslyRefundedExactMinor = 499;
                break;
            case "previous-cash":
                fixture.Intent.PreviouslyRefundedCashMinor = 499;
                break;
            case "refund-exact":
                fixture.Intent.ExactRefundAmountMinor = 251;
                break;
            case "refund-adjustment":
                fixture.Intent.RefundAdjustmentMinor = 1;
                break;
            case "refund-cash":
                fixture.Intent.CashRefundAmountMinor = 251;
                break;
            case "retained-exact":
                fixture.Intent.RetainedExactAmountMinor = 251;
                break;
            case "retained-cash":
                fixture.Intent.RetainedCashDueMinor = 249;
                break;
            case "scope-total":
                fixture.Leg.FrozenScopesJson = SerializeScope(CreateScope(
                    fixture.AllocationId, fixture.TailSourceOrderId, Id(15),
                    startOrdinal: 1, unitCount: 1, amountMinor: 200));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }

        RequireRejected(fixture);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("live-mode")]
    [InlineData("charge")]
    [InlineData("intent")]
    public void Pending_manual_till_tail_rejects_provider_context(string providerField)
    {
        var fixture = CreateFixture();
        switch (providerField)
        {
            case "account":
                fixture.Leg.ProviderAccountId = "acct_test_example";
                break;
            case "live-mode":
                fixture.Leg.ProviderLiveMode = false;
                break;
            case "charge":
                fixture.Leg.ProviderChargeId = "ch_test_example";
                break;
            case "intent":
                fixture.Leg.ProviderIntentId = "pi_test_example";
                break;
        }

        RequireRejected(fixture);
    }

    [Fact]
    public void Pending_tail_rejects_return_evidence()
    {
        var returnEvidence = CreateFixture();
        returnEvidence.Intent.ReturnEvidence = new AccountCashRefundEvidence
        {
            Id = Id(40),
            CreatedBy = Creator,
            IntentId = returnEvidence.Intent.Id,
            ExactRefundAmountMinor = 250,
            CashReturnedMinor = 250,
            Currency = "CHF",
            ActorId = Id(41),
            ActorRole = UserRole.Admin,
            TillReference = "test-till",
            ObservedAt = DateTime.UnixEpoch
        };
        RequireRejected(returnEvidence);
    }

    [Fact]
    public void Pending_tail_rejects_persisted_refund_evidence()
    {
        var fixture = CreateFixture() with
        {
            Evidence = [new OrderAmendmentRefundEvidence
            {
                Id = Id(42),
                CreatedBy = Creator,
                RefundLegId = Id(10),
                Sequence = 1,
                Kind = OrderAmendmentRefundEvidenceKind.ManualTillConfirmation,
                State = OrderAmendmentRefundLegState.Succeeded,
                AmountMinor = 250,
                Currency = "CHF",
                ActorUserId = Id(41),
                ActorRole = UserRole.Admin.ToString(),
                ObservedAt = DateTime.UnixEpoch
            }]
        };
        RequireRejected(fixture);
    }

    [Fact]
    public void Pending_tail_rejects_reversal_rows()
    {
        var fixture = CreateFixture() with
        {
            TailReversals = [NewReversal(Id(22), Id(10), Id(11), Id(5), Id(15),
                startOrdinal: 1, unitCount: 1, amountMinor: 250)]
        };
        RequireRejected(fixture);
    }

    [Fact]
    public void Pending_tail_rejects_overlap_with_prior_history()
    {
        var fixture = CreateFixture();
        fixture.HistoryReversals.Add(NewReversal(Id(23), Id(13), Id(11), Id(5), Id(15),
            startOrdinal: 1, unitCount: 1, amountMinor: 250));

        RequireRejected(fixture);
    }

    [Fact]
    public void Pending_tail_rejects_a_leg_already_owned_by_the_validated_history_prefix()
    {
        var fixture = CreateFixture();
        fixture.PriorOperationIds.Add(fixture.Operation.Id);

        RequireRejected(fixture);
    }

    private static void RequireValid(PendingCashTailCase fixture) =>
        AccountCashRefundHistoryPrefix.RequireUnrelatedPendingTail(
            fixture.Intent, fixture.Leg, fixture.Operation, new AccountCashRefundPendingTailProof(
                fixture.Receipt, fixture.AttemptServiceSessionId, fixture.History, fixture.Evidence,
                fixture.TailReversals, new AccountCashRefundPendingTailScope(
                    fixture.HistoryReversals, fixture.Allocations,
                    fixture.PriorOperationIds, fixture.TargetSourceOrderIds)));

    private static void RequireRejected(PendingCashTailCase fixture)
    {
        Action action = () => RequireValid(fixture);
        action.Should().Throw<ConflictException>();
    }

    private static PendingCashTailCase CreateFixture()
    {
        var attemptId = Id(1);
        var receiptId = Id(2);
        var sessionId = Id(3);
        var targetOrderId = Id(4);
        var tailOrderId = Id(5);
        var targetItemId = Id(6);
        var targetPaymentId = Id(7);
        var tailItemId = Id(15);
        var tailPaymentId = Id(16);
        var tailOperationId = Id(8);
        var priorOperationId = Id(9);
        var tailLegId = Id(10);
        var allocationId = Id(11);
        var targetAllocationId = Id(17);
        var actorId = Id(12);
        var priorLegId = Id(13);

        var receipt = new AccountCashCollectionReceipt
        {
            Id = receiptId,
            CreatedBy = Creator,
            AttemptId = attemptId,
            PolicyVersion = SwissCashPolicy,
            Currency = "CHF",
            PaymentMethod = PaymentMethod.Cash,
            ExactAmountMinor = 1_000,
            AdjustmentMinor = 0,
            DueAmountMinor = 1_000,
            ReceivedMinor = 1_000,
            ChangeMinor = 0,
            CapturedAt = DateTime.UnixEpoch
        };
        var targetAllocation = new AccountPaymentAllocation
        {
            Id = targetAllocationId,
            CreatedBy = Creator,
            AttemptId = attemptId,
            OrderId = targetOrderId,
            OrderItemId = targetItemId,
            OrderPaymentId = targetPaymentId,
            StartOrdinal = 1,
            UnitCount = 2,
            MinorPerUnit = 250,
            AmountMinor = 500
        };
        var tailAllocation = new AccountPaymentAllocation
        {
            Id = allocationId,
            CreatedBy = Creator,
            AttemptId = attemptId,
            OrderId = tailOrderId,
            OrderItemId = tailItemId,
            OrderPaymentId = tailPaymentId,
            StartOrdinal = 1,
            UnitCount = 2,
            MinorPerUnit = 250,
            AmountMinor = 500
        };
        var leg = new OrderAmendmentRefundLeg
        {
            Id = tailLegId,
            CreatedBy = Creator,
            OperationId = tailOperationId,
            SourcePaymentId = tailPaymentId,
            AccountPaymentAttemptId = attemptId,
            Custody = OrderAmendmentRefundCustody.ManualTill,
            State = OrderAmendmentRefundLegState.Pending,
            AmountMinor = 250,
            Currency = "CHF",
            FrozenScopesJson = SerializeScope(CreateScope(
                allocationId, tailOrderId, tailItemId,
                startOrdinal: 1, unitCount: 1, amountMinor: 250))
        };
        var operation = new OrderAmendmentResolutionOperation
        {
            Id = tailOperationId,
            CreatedBy = Creator,
            SourceOrderId = tailOrderId,
            ServiceSessionId = sessionId,
            ActorUserId = actorId,
            ActorRole = UserRole.Admin.ToString(),
            Currency = "CHF",
            RefundMinor = 250,
            State = OrderAmendmentResolutionOperationState.Processing,
            StartedAt = DateTime.UnixEpoch
        };
        var intent = new AccountCashRefundIntent
        {
            Id = Id(14),
            CreatedBy = Creator,
            RefundLegId = tailLegId,
            OperationId = tailOperationId,
            AttemptId = attemptId,
            CollectionReceiptId = receiptId,
            PolicyVersion = SwissCashPolicy,
            Currency = "CHF",
            OriginalExactAmountMinor = 1_000,
            OriginalAdjustmentMinor = 0,
            OriginalDueAmountMinor = 1_000,
            PreviouslyRefundedExactMinor = 500,
            PreviouslyRefundedCashMinor = 500,
            ExactRefundAmountMinor = 250,
            RefundAdjustmentMinor = 0,
            CashRefundAmountMinor = 250,
            RetainedExactAmountMinor = 250,
            RetainedCashDueMinor = 250,
            PriorHistoryFingerprint = FingerprintBeforePendingLeg
        };
        var priorReversal = NewReversal(Id(21), priorLegId, targetAllocationId, targetOrderId, targetItemId,
            startOrdinal: 1, unitCount: 2, amountMinor: 500);

        return new PendingCashTailCase(
            intent, leg, operation, receipt, sessionId,
            new AccountCashRefundHistory(500, 500, FingerprintBeforePendingLeg),
            [], [], [priorReversal],
            new Dictionary<Guid, AccountPaymentAllocation>
            {
                [targetAllocationId] = targetAllocation,
                [allocationId] = tailAllocation
            },
            new HashSet<Guid> { priorOperationId }, new HashSet<Guid> { targetOrderId },
            targetOrderId, tailOrderId, allocationId);
    }

    private static OrderAmendmentRefundScope CreateScope(
        Guid allocationId, Guid orderId, Guid itemId, int startOrdinal, int unitCount, long amountMinor) =>
        new(allocationId, orderId, itemId, startOrdinal, unitCount, 250, amountMinor);

    private static string SerializeScope(OrderAmendmentRefundScope scope) =>
        OrderAmendmentJson.Serialize(new[] { scope });

    private static AccountPaymentAllocationReversal NewReversal(
        Guid reversalId, Guid legId, Guid allocationId, Guid orderId, Guid? itemId,
        int startOrdinal, int unitCount, long amountMinor) => new()
        {
            Id = reversalId,
            CreatedBy = Creator,
            AllocationId = allocationId,
            RefundLegId = legId,
            OrderId = orderId,
            OrderItemId = itemId,
            StartOrdinal = startOrdinal,
            UnitCount = unitCount,
            MinorPerUnit = 250,
            AmountMinor = amountMinor,
            Currency = "CHF",
            ActorUserId = Id(12),
            ActorRole = UserRole.Admin.ToString(),
            ReversedAt = DateTime.UnixEpoch
        };

    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:x12}");

    private sealed record PendingCashTailCase(
        AccountCashRefundIntent Intent,
        OrderAmendmentRefundLeg Leg,
        OrderAmendmentResolutionOperation Operation,
        AccountCashCollectionReceipt Receipt,
        Guid AttemptServiceSessionId,
        AccountCashRefundHistory History,
        IReadOnlyList<OrderAmendmentRefundEvidence> Evidence,
        IReadOnlyList<AccountPaymentAllocationReversal> TailReversals,
        List<AccountPaymentAllocationReversal> HistoryReversals,
        Dictionary<Guid, AccountPaymentAllocation> Allocations,
        HashSet<Guid> PriorOperationIds,
        HashSet<Guid> TargetSourceOrderIds,
        Guid TargetSourceOrderId,
        Guid TailSourceOrderId,
        Guid AllocationId);
}
