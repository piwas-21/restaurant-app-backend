using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;

/// <summary>Reconciles exact account cash tenders to attested physical cash movements.</summary>
internal static class ZReportCashTenderAdjustmentReader
{
    internal static async Task<IReadOnlyDictionary<string, long>> ReadAsync(
        ApplicationDbContext context,
        DateTime startUtc,
        DateTime endUtc,
        IReadOnlyCollection<RefundedCashPayment> refundedCashPayments,
        CancellationToken cancellationToken)
    {
        var adjustments = new Dictionary<string, long>(StringComparer.Ordinal);
        await AddCollectionAdjustmentsAsync(context, startUtc, endUtc, adjustments, cancellationToken);
        await AddPhysicalRefundsAsync(context, startUtc, endUtc, adjustments, cancellationToken);
        await AddFinalizedRefundAdjustmentsAsync(
            context, refundedCashPayments, adjustments, cancellationToken);
        return adjustments;
    }

    private static async Task AddCollectionAdjustmentsAsync(
        ApplicationDbContext context,
        DateTime startUtc,
        DateTime endUtc,
        Dictionary<string, long> adjustments,
        CancellationToken cancellationToken)
    {
        var receipts = await context.AccountCashCollectionReceipts.AsNoTracking()
            .Where(value => value.PaymentMethod == PaymentMethod.Cash
                && value.CapturedAt >= startUtc && value.CapturedAt < endUtc)
            .GroupBy(value => value.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                AdjustmentMinor = group.Sum(value =>
                    value.ReceivedMinor - value.ChangeMinor - value.ExactAmountMinor)
            })
            .ToArrayAsync(cancellationToken);

        foreach (var receipt in receipts)
            AddMinor(adjustments, CurrencyKey(receipt.Currency), receipt.AdjustmentMinor);
    }

    private static async Task AddPhysicalRefundsAsync(
        ApplicationDbContext context,
        DateTime startUtc,
        DateTime endUtc,
        Dictionary<string, long> adjustments,
        CancellationToken cancellationToken)
    {
        var returns = await context.AccountCashRefundEvidence.AsNoTracking()
            .Where(value => value.ObservedAt >= startUtc && value.ObservedAt < endUtc)
            .GroupBy(value => value.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                CashReturnedMinor = group.Sum(value => value.CashReturnedMinor)
            })
            .ToArrayAsync(cancellationToken);

        foreach (var returned in returns)
            AddMinor(adjustments, CurrencyKey(returned.Currency), checked(-returned.CashReturnedMinor));
    }

    private static async Task AddFinalizedRefundAdjustmentsAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<RefundedCashPayment> refundedCashPayments,
        Dictionary<string, long> adjustments,
        CancellationToken cancellationToken)
    {
        if (refundedCashPayments.Count == 0)
            return;

        var paymentsById = refundedCashPayments.ToDictionary(value => value.PaymentId);
        var paymentIds = paymentsById.Keys.ToArray();
        var refunds = await (
            from leg in context.OrderAmendmentRefundLegs.AsNoTracking()
            join payment in context.OrderPayments.AsNoTracking()
                on leg.SourcePaymentId equals payment.Id
            join operation in context.OrderAmendmentResolutionOperations.AsNoTracking()
                on leg.OperationId equals operation.Id
            join intent in context.AccountCashRefundIntents.AsNoTracking()
                on leg.Id equals intent.RefundLegId
            join evidence in context.AccountCashRefundEvidence.AsNoTracking()
                on intent.Id equals evidence.IntentId
            join receipt in context.AccountCashCollectionReceipts.AsNoTracking()
                on intent.CollectionReceiptId equals receipt.Id
            where paymentIds.Contains(leg.SourcePaymentId)
                && payment.OrderId == operation.SourceOrderId
                && leg.Custody == OrderAmendmentRefundCustody.ManualTill
                && leg.State == OrderAmendmentRefundLegState.Succeeded
                && leg.ResolvedAt != null
                && operation.State == OrderAmendmentResolutionOperationState.Resolved
                && operation.ResolvedAt != null
                && operation.ActorRole == UserRole.Admin.ToString()
                && intent.OperationId == operation.Id
                && intent.AttemptId == leg.AccountPaymentAttemptId
                && receipt.AttemptId == intent.AttemptId
                && leg.AmountMinor == intent.ExactRefundAmountMinor
                && evidence.ExactRefundAmountMinor == intent.ExactRefundAmountMinor
                && evidence.Currency == intent.Currency
                && evidence.ActorId == operation.ActorUserId
                && evidence.ActorRole == UserRole.Admin
                && evidence.ObservedAt == leg.ResolvedAt
                && evidence.TillReference == leg.ManualTillReference
                && receipt.Currency == intent.Currency
                && receipt.PaymentMethod == PaymentMethod.Cash
            select new FinalizedCashRefund(
                leg.SourcePaymentId, leg.Currency, intent.Currency, intent.ExactRefundAmountMinor))
            .ToArrayAsync(cancellationToken);

        foreach (var group in refunds.GroupBy(value => value.PaymentId))
        {
            var payment = paymentsById[group.Key];
            var paymentCurrency = CurrencyKey(payment.Currency);
            if (paymentCurrency.Length == 0)
                continue;

            long exactRefundMinor = 0;
            foreach (var refund in group)
            {
                if (CurrencyKey(refund.LegCurrency) != paymentCurrency
                    || CurrencyKey(refund.IntentCurrency) != paymentCurrency)
                    continue;

                exactRefundMinor = checked(exactRefundMinor + refund.ExactRefundMinor);
            }

            // RefundedAmount is cumulative. If evidence cannot fit inside that exact total,
            // keep the existing exact refund treatment instead of guessing which amount/currency
            // the database row represents.
            if (exactRefundMinor <= 0 || exactRefundMinor > payment.RefundedMinor)
                continue;

            AddMinor(adjustments, paymentCurrency, exactRefundMinor);
        }
    }

    private static string CurrencyKey(string? currency) =>
        CurrencyCode.Normalize(currency) ?? string.Empty;

    private static void AddMinor(Dictionary<string, long> totals, string currency, long amountMinor)
    {
        if (amountMinor == 0)
            return;

        totals.TryGetValue(currency, out var current);
        totals[currency] = checked(current + amountMinor);
    }

    internal sealed record RefundedCashPayment(Guid PaymentId, string? Currency, long RefundedMinor);

    private sealed record FinalizedCashRefund(
        Guid PaymentId, string? LegCurrency, string? IntentCurrency, long ExactRefundMinor);
}
