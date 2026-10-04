using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;

internal static class ZReportAccountCashMovementReader
{
    private const string CoverageNote =
        "Table-account cash receipt and return evidence only; this is not the whole restaurant till. Unconfirmed cash is expected, not a recorded movement.";

    internal static async Task<ZReportAccountCashMovementDto> ReadAsync(
        ApplicationDbContext context, DateTime startUtc, DateTime endUtc,
        DateTime snapshotAtUtc, CancellationToken cancellationToken)
    {
        var collections = await context.AccountCashCollectionReceipts.AsNoTracking()
            .Where(value => value.CapturedAt >= startUtc && value.CapturedAt < endUtc)
            .ToArrayAsync(cancellationToken);
        var returns = await context.AccountCashRefundEvidence.AsNoTracking()
            .Where(value => value.ObservedAt >= startUtc && value.ObservedAt < endUtc)
            .ToArrayAsync(cancellationToken);
        var legacyCaptures = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.PaymentMethod == PaymentMethod.Cash
                && value.State == AccountPaymentState.Captured
                && value.CashCollectionReceipt == null
                && value.CompletedAt >= startUtc && value.CompletedAt < endUtc)
            .ToArrayAsync(cancellationToken);
        var legacyReturns = await (
            from evidence in context.OrderAmendmentRefundEvidence.AsNoTracking()
            join leg in context.OrderAmendmentRefundLegs.AsNoTracking()
                on evidence.RefundLegId equals leg.Id
            join attempt in context.AccountPaymentAttempts.AsNoTracking()
                on leg.AccountPaymentAttemptId equals (Guid?)attempt.Id
            where evidence.Kind == OrderAmendmentRefundEvidenceKind.ManualTillConfirmation
                && evidence.State == OrderAmendmentRefundLegState.Succeeded
                && evidence.ObservedAt >= startUtc && evidence.ObservedAt < endUtc
                && attempt.PaymentMethod == PaymentMethod.Cash && leg.CashRefundIntent == null
            select new LegacyCashReturn(evidence.Currency, evidence.AmountMinor))
            .ToArrayAsync(cancellationToken);
        var unresolved = await context.AccountCashRefundIntents.AsNoTracking()
            .Where(value => value.Operation!.State != OrderAmendmentResolutionOperationState.Resolved
                || value.ReturnEvidence == null)
            .Select(value => new UnresolvedCashRefund(value.Currency,
                value.ExactRefundAmountMinor,
                value.ReturnEvidence == null ? value.CashRefundAmountMinor : 0))
            .ToArrayAsync(cancellationToken);

        var currencies = collections.Select(value => value.Currency)
            .Concat(returns.Select(value => value.Currency))
            .Concat(legacyCaptures.Select(value => value.Currency))
            .Concat(legacyReturns.Select(value => value.Currency))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(currency => BuildCurrency(currency,
                collections.Where(value => value.Currency == currency),
                returns.Where(value => value.Currency == currency),
                legacyCaptures.Where(value => value.Currency == currency),
                legacyReturns.Where(value => value.Currency == currency)))
            .ToArray();
        var unresolvedByCurrency = unresolved.GroupBy(value => value.Currency, StringComparer.Ordinal)
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(group => new ZReportAccountCashCurrencyDto(
                Currency: group.Key, CollectionCount: 0, CollectedExactMinor: 0,
                CashReceivedMinor: 0, ChangeReturnedMinor: 0, CashDueMinor: 0,
                ReturnCount: 0, ExactRefundedMinor: 0, PhysicalCashReturnedMinor: 0,
                RefundAdjustmentMinor: 0, LegacyCaptureWithoutReceiptCount: 0,
                LegacyCaptureExactMinor: 0, LegacyReturnWithoutPhysicalEvidenceCount: 0,
                LegacyExactRefundMinor: 0, UnresolvedReturnCount: group.Count(),
                UnresolvedExactRefundMinor: group.Sum(value => value.ExactRefundMinor),
                UnconfirmedPhysicalCashMinor: group.Sum(value => value.PhysicalCashMinor)))
            .ToArray();
        return new ZReportAccountCashMovementDto(snapshotAtUtc, CoverageNote,
            CoversWholeRestaurantTill: false, currencies, unresolvedByCurrency);
    }

    private static ZReportAccountCashCurrencyDto BuildCurrency(
        string currency, IEnumerable<RestaurantSystem.Domain.Entities.AccountCashCollectionReceipt> collections,
        IEnumerable<RestaurantSystem.Domain.Entities.AccountCashRefundEvidence> returns,
        IEnumerable<RestaurantSystem.Domain.Entities.AccountPaymentAttempt> legacyCaptures,
        IEnumerable<LegacyCashReturn> legacyReturns)
    {
        var collectionRows = collections.ToArray();
        var returnRows = returns.ToArray();
        var legacyRows = legacyCaptures.ToArray();
        var legacyReturnRows = legacyReturns.ToArray();
        return new ZReportAccountCashCurrencyDto(
            Currency: currency,
            CollectionCount: collectionRows.Length,
            CollectedExactMinor: collectionRows.Sum(value => value.ExactAmountMinor),
            CashReceivedMinor: collectionRows.Sum(value => value.ReceivedMinor),
            ChangeReturnedMinor: collectionRows.Sum(value => value.ChangeMinor),
            CashDueMinor: collectionRows.Sum(value => value.DueAmountMinor),
            ReturnCount: returnRows.Length,
            ExactRefundedMinor: returnRows.Sum(value => value.ExactRefundAmountMinor),
            PhysicalCashReturnedMinor: returnRows.Sum(value => value.CashReturnedMinor),
            RefundAdjustmentMinor: returnRows.Sum(value => value.RefundAdjustmentMinor),
            LegacyCaptureWithoutReceiptCount: legacyRows.Length,
            LegacyCaptureExactMinor: legacyRows.Sum(value => value.AmountMinor),
            LegacyReturnWithoutPhysicalEvidenceCount: legacyReturnRows.Length,
            LegacyExactRefundMinor: legacyReturnRows.Sum(value => value.ExactRefundMinor),
            UnresolvedReturnCount: 0, UnresolvedExactRefundMinor: 0,
            UnconfirmedPhysicalCashMinor: 0);
    }

    private sealed record UnresolvedCashRefund(string Currency,
        long ExactRefundMinor, long PhysicalCashMinor);

    private sealed record LegacyCashReturn(string Currency, long ExactRefundMinor);
}
