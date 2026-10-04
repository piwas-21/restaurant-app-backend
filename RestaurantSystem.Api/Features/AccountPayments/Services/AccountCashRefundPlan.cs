using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal sealed record AccountCashRefundPlan(
    Guid ReceiptId,
    Guid AttemptId,
    string PolicyVersion,
    string Currency,
    long OriginalExactAmountMinor,
    long OriginalAdjustmentMinor,
    long OriginalDueAmountMinor,
    long PreviouslyRefundedExactMinor,
    long PreviouslyRefundedCashMinor,
    long ExactRefundAmountMinor,
    long RefundAdjustmentMinor,
    long CashRefundAmountMinor,
    long RetainedExactAmountMinor,
    long RetainedCashDueMinor,
    string PriorHistoryFingerprint)
{
    internal static AccountCashRefundPlan Create(
        AccountCashCollectionReceipt receipt,
        AccountCashRefundHistory history,
        CashRefundSettlementQuote refund) => new(
        receipt.Id, receipt.AttemptId, receipt.PolicyVersion, receipt.Currency,
        receipt.ExactAmountMinor, receipt.AdjustmentMinor, receipt.DueAmountMinor,
        history.RefundedExactMinor, history.RefundedCashMinor,
        refund.ExactRefundAmountMinor, refund.RefundAdjustmentMinor,
        refund.CashRefundAmountMinor, refund.RetainedExactAmountMinor,
        refund.RetainedCashDueMinor, history.Fingerprint);

    internal static AccountCashRefundPlan FromIntent(AccountCashRefundIntent intent) => new(
        intent.CollectionReceiptId, intent.AttemptId, intent.PolicyVersion, intent.Currency,
        intent.OriginalExactAmountMinor, intent.OriginalAdjustmentMinor, intent.OriginalDueAmountMinor,
        intent.PreviouslyRefundedExactMinor, intent.PreviouslyRefundedCashMinor,
        intent.ExactRefundAmountMinor, intent.RefundAdjustmentMinor, intent.CashRefundAmountMinor,
        intent.RetainedExactAmountMinor, intent.RetainedCashDueMinor,
        intent.PriorHistoryFingerprint);

    internal CashRefundSettlementQuote SettlementQuote() => new(
        PolicyVersion, Currency, PaymentMethod.Cash,
        ExactRefundAmountMinor, RefundAdjustmentMinor, CashRefundAmountMinor,
        RetainedExactAmountMinor, RetainedCashDueMinor);
}

internal sealed record AccountCashRefundHistory(
    long RefundedExactMinor,
    long RefundedCashMinor,
    string Fingerprint);
