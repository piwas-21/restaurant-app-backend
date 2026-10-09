using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Api.Features.OrderAmendments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountCashRefundHistoryFingerprint
{
    internal static string Seed(AccountCashCollectionReceipt receipt) => OrderAmendmentJson.Hash(
        OrderAmendmentJson.Serialize(new SeedSnapshot(receipt.Id, receipt.AttemptId,
            receipt.PolicyVersion, receipt.Currency, receipt.ExactAmountMinor,
            receipt.AdjustmentMinor, receipt.DueAmountMinor, receipt.ReceivedMinor,
            receipt.ChangeMinor, receipt.RequestHash, receipt.ActorId, receipt.ActorRole.ToString(),
            receipt.CapturedAt)));

    internal static string Advance(string priorFingerprint, AccountCashRefundIntent intent,
        AccountCashRefundEvidence evidence, IReadOnlyList<OrderAmendmentRefundScope> scopes,
        IReadOnlyList<AccountPaymentAllocationReversal> reversals) => OrderAmendmentJson.Hash(
        OrderAmendmentJson.Serialize(new EntrySnapshot(priorFingerprint, intent.Id,
            intent.RefundLegId, intent.OperationId, intent.AttemptId, intent.CollectionReceiptId,
            intent.PolicyVersion, intent.Currency, intent.OriginalExactAmountMinor,
            intent.OriginalAdjustmentMinor, intent.OriginalDueAmountMinor,
            intent.PreviouslyRefundedExactMinor, intent.PreviouslyRefundedCashMinor,
            intent.ExactRefundAmountMinor, intent.RefundAdjustmentMinor,
            intent.CashRefundAmountMinor, intent.RetainedExactAmountMinor,
            intent.RetainedCashDueMinor, evidence.Id, evidence.ExactRefundAmountMinor,
            evidence.RefundAdjustmentMinor, evidence.CashReturnedMinor, evidence.Currency,
            evidence.ActorId, evidence.ActorRole.ToString(), evidence.TillReference,
            evidence.ObservedAt, scopes.OrderBy(value => value.AllocationId)
                .ThenBy(value => value.StartOrdinal).Select(ScopeSnapshot.From).ToArray(),
            reversals.OrderBy(value => value.AllocationId).ThenBy(value => value.StartOrdinal)
                .Select(ReversalSnapshot.From).ToArray())));

    private sealed record SeedSnapshot(
        Guid ReceiptId, Guid AttemptId, string PolicyVersion, string Currency,
        long ExactAmountMinor, long AdjustmentMinor, long DueAmountMinor,
        long ReceivedMinor, long ChangeMinor, string RequestHash, Guid ActorId,
        string ActorRole, DateTime CapturedAt);

    private sealed record EntrySnapshot(
        string PriorFingerprint, Guid IntentId, Guid RefundLegId, Guid OperationId,
        Guid AttemptId, Guid ReceiptId, string PolicyVersion, string Currency,
        long OriginalExactMinor, long OriginalAdjustmentMinor, long OriginalDueMinor,
        long PriorExactMinor, long PriorCashMinor, long ExactRefundMinor,
        long RefundAdjustmentMinor, long CashRefundMinor, long RetainedExactMinor,
        long RetainedCashMinor, Guid EvidenceId, long EvidenceExactMinor,
        long EvidenceAdjustmentMinor, long EvidenceCashMinor, string EvidenceCurrency,
        Guid EvidenceActorId, string EvidenceActorRole, string TillReference,
        DateTime ObservedAt, IReadOnlyList<ScopeSnapshot> Scopes,
        IReadOnlyList<ReversalSnapshot> Reversals);

    private sealed record ScopeSnapshot(Guid AllocationId, Guid OrderId, Guid? OrderItemId,
        int StartOrdinal, int UnitCount, long MinorPerUnit, long AmountMinor)
    {
        internal static ScopeSnapshot From(OrderAmendmentRefundScope value) => new(
            value.AllocationId, value.OrderId, value.OrderItemId, value.StartOrdinal,
            value.UnitCount, value.MinorPerUnit, value.AmountMinor);
    }

    private sealed record ReversalSnapshot(Guid Id, Guid AllocationId, Guid RefundLegId,
        Guid OrderId, Guid? OrderItemId, int StartOrdinal, int UnitCount,
        long MinorPerUnit, long AmountMinor, string Currency, Guid ActorUserId,
        string ActorRole, DateTime ReversedAt)
    {
        internal static ReversalSnapshot From(AccountPaymentAllocationReversal value) => new(
            value.Id, value.AllocationId, value.RefundLegId, value.OrderId,
            value.OrderItemId, value.StartOrdinal, value.UnitCount, value.MinorPerUnit,
            value.AmountMinor, value.Currency, value.ActorUserId, value.ActorRole,
            value.ReversedAt);
    }
}
