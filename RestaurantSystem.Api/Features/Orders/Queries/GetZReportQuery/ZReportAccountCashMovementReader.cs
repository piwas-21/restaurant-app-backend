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
            .GroupBy(value => value.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                Count = group.Count(),
                ExactAmountMinor = group.Sum(value => value.ExactAmountMinor),
                ReceivedMinor = group.Sum(value => value.ReceivedMinor),
                ChangeMinor = group.Sum(value => value.ChangeMinor),
                DueAmountMinor = group.Sum(value => value.DueAmountMinor)
            })
            .ToArrayAsync(cancellationToken);
        var returns = await context.AccountCashRefundEvidence.AsNoTracking()
            .Where(value => value.ObservedAt >= startUtc && value.ObservedAt < endUtc)
            .GroupBy(value => value.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                Count = group.Count(),
                ExactRefundAmountMinor = group.Sum(value => value.ExactRefundAmountMinor),
                CashReturnedMinor = group.Sum(value => value.CashReturnedMinor),
                RefundAdjustmentMinor = group.Sum(value => value.RefundAdjustmentMinor)
            })
            .ToArrayAsync(cancellationToken);
        var legacyCaptures = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.PaymentMethod == PaymentMethod.Cash
                && value.State == AccountPaymentState.Captured
                && value.CashCollectionReceipt == null
                && value.CompletedAt >= startUtc && value.CompletedAt < endUtc)
            .GroupBy(value => value.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                Count = group.Count(),
                ExactAmountMinor = group.Sum(value => value.AmountMinor)
            })
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
            select new { evidence.Currency, ExactRefundMinor = evidence.AmountMinor })
            .GroupBy(value => value.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                Count = group.Count(),
                ExactRefundMinor = group.Sum(value => value.ExactRefundMinor)
            })
            .ToArrayAsync(cancellationToken);
        var unresolved = await context.AccountCashRefundIntents.AsNoTracking()
            .Where(value => value.Operation!.State != OrderAmendmentResolutionOperationState.Resolved
                || value.ReturnEvidence == null)
            .Select(value => new
            {
                value.Currency,
                ExactRefundMinor = value.ExactRefundAmountMinor,
                PhysicalCashMinor = value.ReturnEvidence == null ? value.CashRefundAmountMinor : 0
            })
            .GroupBy(value => value.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                Count = group.Count(),
                ExactRefundMinor = group.Sum(value => value.ExactRefundMinor),
                PhysicalCashMinor = group.Sum(value => value.PhysicalCashMinor)
            })
            .ToArrayAsync(cancellationToken);

        var collectionByCurrency = collections.ToDictionary(value => value.Currency, StringComparer.Ordinal);
        var returnByCurrency = returns.ToDictionary(value => value.Currency, StringComparer.Ordinal);
        var legacyCaptureByCurrency = legacyCaptures.ToDictionary(value => value.Currency, StringComparer.Ordinal);
        var legacyReturnByCurrency = legacyReturns.ToDictionary(value => value.Currency, StringComparer.Ordinal);
        var currencies = collections.Select(value => value.Currency)
            .Concat(returns.Select(value => value.Currency))
            .Concat(legacyCaptures.Select(value => value.Currency))
            .Concat(legacyReturns.Select(value => value.Currency))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(currency =>
            {
                collectionByCurrency.TryGetValue(currency, out var collection);
                returnByCurrency.TryGetValue(currency, out var cashReturn);
                legacyCaptureByCurrency.TryGetValue(currency, out var legacyCapture);
                legacyReturnByCurrency.TryGetValue(currency, out var legacyReturn);
                return new ZReportAccountCashCurrencyDto(
                    Currency: currency,
                    CollectionCount: collection?.Count ?? 0,
                    CollectedExactMinor: collection?.ExactAmountMinor ?? 0,
                    CashReceivedMinor: collection?.ReceivedMinor ?? 0,
                    ChangeReturnedMinor: collection?.ChangeMinor ?? 0,
                    CashDueMinor: collection?.DueAmountMinor ?? 0,
                    ReturnCount: cashReturn?.Count ?? 0,
                    ExactRefundedMinor: cashReturn?.ExactRefundAmountMinor ?? 0,
                    PhysicalCashReturnedMinor: cashReturn?.CashReturnedMinor ?? 0,
                    RefundAdjustmentMinor: cashReturn?.RefundAdjustmentMinor ?? 0,
                    LegacyCaptureWithoutReceiptCount: legacyCapture?.Count ?? 0,
                    LegacyCaptureExactMinor: legacyCapture?.ExactAmountMinor ?? 0,
                    LegacyReturnWithoutPhysicalEvidenceCount: legacyReturn?.Count ?? 0,
                    LegacyExactRefundMinor: legacyReturn?.ExactRefundMinor ?? 0,
                    UnresolvedReturnCount: 0, UnresolvedExactRefundMinor: 0,
                    UnconfirmedPhysicalCashMinor: 0);
            })
            .ToArray();
        var unresolvedByCurrency = unresolved
            .OrderBy(value => value.Currency, StringComparer.Ordinal)
            .Select(value => new ZReportAccountCashCurrencyDto(
                Currency: value.Currency, CollectionCount: 0, CollectedExactMinor: 0,
                CashReceivedMinor: 0, ChangeReturnedMinor: 0, CashDueMinor: 0,
                ReturnCount: 0, ExactRefundedMinor: 0, PhysicalCashReturnedMinor: 0,
                RefundAdjustmentMinor: 0, LegacyCaptureWithoutReceiptCount: 0,
                LegacyCaptureExactMinor: 0, LegacyReturnWithoutPhysicalEvidenceCount: 0,
                LegacyExactRefundMinor: 0, UnresolvedReturnCount: value.Count,
                UnresolvedExactRefundMinor: value.ExactRefundMinor,
                UnconfirmedPhysicalCashMinor: value.PhysicalCashMinor))
            .ToArray();
        return new ZReportAccountCashMovementDto(snapshotAtUtc, CoverageNote,
            CoversWholeRestaurantTill: false, currencies, unresolvedByCurrency);
    }
}
