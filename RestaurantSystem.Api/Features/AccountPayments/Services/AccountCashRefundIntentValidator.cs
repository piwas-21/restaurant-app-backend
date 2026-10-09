using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountCashRefundIntentValidator
{
    internal static CashRefundSettlementQuote RequireMatchesHistory(
        AccountCashRefundIntent intent, OrderAmendmentRefundLeg leg,
        OrderAmendmentResolutionOperation operation, AccountCashCollectionReceipt receipt,
        AccountCashRefundHistory history)
    {
        var original = new CashSettlementQuote(receipt.PolicyVersion, receipt.Currency,
            receipt.PaymentMethod, receipt.ExactAmountMinor, receipt.AdjustmentMinor,
            receipt.DueAmountMinor);
        var expected = AccountCashRefundPolicy.Project(original,
            history.RefundedExactMinor, history.RefundedCashMinor, leg.AmountMinor);
        if (intent.RefundLegId != leg.Id || intent.OperationId != operation.Id
            || intent.OperationId != leg.OperationId
            || intent.AttemptId != receipt.AttemptId || leg.AccountPaymentAttemptId != receipt.AttemptId
            || intent.CollectionReceiptId != receipt.Id || intent.PolicyVersion != expected.PolicyVersion
            || intent.Currency != expected.Currency || operation.Currency != receipt.Currency
            || intent.OriginalExactAmountMinor != receipt.ExactAmountMinor
            || intent.OriginalAdjustmentMinor != receipt.AdjustmentMinor
            || intent.OriginalDueAmountMinor != receipt.DueAmountMinor
            || intent.PreviouslyRefundedExactMinor != history.RefundedExactMinor
            || intent.PreviouslyRefundedCashMinor != history.RefundedCashMinor
            || intent.PriorHistoryFingerprint != history.Fingerprint
            || intent.ExactRefundAmountMinor != expected.ExactRefundAmountMinor
            || intent.RefundAdjustmentMinor != expected.RefundAdjustmentMinor
            || intent.CashRefundAmountMinor != expected.CashRefundAmountMinor
            || intent.RetainedExactAmountMinor != expected.RetainedExactAmountMinor
            || intent.RetainedCashDueMinor != expected.RetainedCashDueMinor
            || leg.Custody != OrderAmendmentRefundCustody.ManualTill
            || leg.Currency != expected.Currency || leg.AmountMinor != expected.ExactRefundAmountMinor
            || operation.ActorUserId == Guid.Empty || operation.ActorRole != UserRole.Admin.ToString())
            throw ReconciliationRequired();
        return expected;
    }

    internal static void RequireReturnEvidence(
        AccountCashRefundIntent intent, OrderAmendmentRefundLeg leg,
        OrderAmendmentResolutionOperation operation, CashRefundSettlementQuote expected,
        AccountCashRefundEvidence? evidence)
    {
        if (evidence is null || evidence.IntentId != intent.Id
            || evidence.ExactRefundAmountMinor != expected.ExactRefundAmountMinor
            || evidence.RefundAdjustmentMinor != expected.RefundAdjustmentMinor
            || evidence.CashReturnedMinor != expected.CashRefundAmountMinor
            || evidence.Currency != expected.Currency || evidence.ActorId != operation.ActorUserId
            || evidence.ActorRole != UserRole.Admin || evidence.TillReference != leg.ManualTillReference
            || evidence.ObservedAt != leg.ResolvedAt || leg.State != OrderAmendmentRefundLegState.Succeeded)
            throw ReconciliationRequired();
    }

    internal static ConflictException ReconciliationRequired() =>
        new("The original cash receipt has unresolved or inconsistent refund history.");
}
