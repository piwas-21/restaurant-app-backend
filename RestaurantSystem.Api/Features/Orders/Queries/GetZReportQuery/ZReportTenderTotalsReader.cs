using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;

internal sealed record ZReportTenderTotals(
    IReadOnlyList<ZReportPaymentMethodDto> PaymentsByMethod,
    IReadOnlyList<ZReportCurrencyAmountDto> StaffTipsCollected,
    IReadOnlyList<ZReportCurrencyAmountDto> StaffTipsRefunded,
    IReadOnlyList<ZReportCurrencyAmountDto> NetCashCollected);

/// <summary>Builds tender totals from capture/refund event timestamps, separately from order sales.</summary>
internal static class ZReportTenderTotalsReader
{
    internal static async Task<ZReportTenderTotals> ReadAsync(
        ApplicationDbContext context,
        DateTime startUtc,
        DateTime endUtc,
        IOrderDisplayCurrencyResolver currencyResolver,
        CancellationToken cancellationToken)
    {
        var payments = await context.OrderPayments.AsNoTracking()
            .Include(payment => payment.Order)
                .ThenInclude(order => order.ExternalReference)
            .Where(payment => !payment.Order.IsDeleted
                && (payment.Status == PaymentStatus.Completed
                    || payment.Status == PaymentStatus.PartiallyRefunded
                    || payment.Status == PaymentStatus.Refunded)
                && ((payment.PaymentDate >= startUtc && payment.PaymentDate < endUtc)
                    || (payment.RefundDate >= startUtc && payment.RefundDate < endUtc)))
            .ToArrayAsync(cancellationToken);

        var tableTips = await ZReportTableAccountTipReader.ReadAsync(
            context, startUtc, endUtc, cancellationToken);
        var captured = payments.Where(payment => payment.PaymentDate >= startUtc
            && payment.PaymentDate < endUtc && payment.Status.IsCaptured()).ToArray();
        var refunded = payments.Where(payment => payment.RefundDate >= startUtc
            && payment.RefundDate < endUtc).ToArray();

        var methodTotals = new Dictionary<(PaymentMethod Method, string Currency), TenderMethodTotal>();
        var collectedTips = new Dictionary<string, long>(StringComparer.Ordinal);
        var refundedTips = new Dictionary<string, long>(StringComparer.Ordinal);
        var netCash = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var payment in captured)
        {
            var currency = ResolveCurrency(payment, currencyResolver);
            var currencyKey = CurrencyKey(currency);
            if (payment.Order.Status != OrderStatus.Cancelled)
            {
                var key = (payment.PaymentMethod, currencyKey);
                if (!methodTotals.TryGetValue(key, out var total))
                {
                    total = new TenderMethodTotal();
                    methodTotals.Add(key, total);
                }

                total.TransactionCount++;
                total.OrderAmount += payment.Amount;
                total.TipMinor = checked(total.TipMinor + payment.TipMinor);
            }

            // The sales-by-method breakdown keeps its established cancelled-order exclusion,
            // while movement and gratuity totals below follow the money actually taken.
            AddMinor(collectedTips, currencyKey, payment.TipMinor);

            if (payment.PaymentMethod == PaymentMethod.Cash)
            {
                AddMinor(netCash, currencyKey, ToMinor(payment.Amount) + payment.TipMinor);
            }
        }

        foreach (var payment in refunded)
        {
            var currency = ResolveCurrency(payment, currencyResolver);
            var currencyKey = CurrencyKey(currency);
            AddMinor(refundedTips, currencyKey, payment.RefundedTipMinor);
            if (payment.PaymentMethod == PaymentMethod.Cash)
            {
                AddMinor(netCash, currencyKey,
                    -(ToMinor(payment.RefundedAmount ?? 0m) + payment.RefundedTipMinor));
            }
        }

        foreach (var tableTip in tableTips)
        {
            var currency = CurrencyCode.Normalize(tableTip.Currency);
            var currencyKey = CurrencyKey(currency);
            var key = (tableTip.PaymentMethod, currencyKey);
            if (!methodTotals.TryGetValue(key, out var total))
            {
                // Table/account tip rows supplement the allocated OrderPayment amount. They are
                // not additional tenders and therefore must not increase TransactionCount.
                total = new TenderMethodTotal();
                methodTotals.Add(key, total);
            }

            total.TipMinor = checked(total.TipMinor + tableTip.TipMinor);
            AddMinor(collectedTips, currencyKey, tableTip.TipMinor);
            if (tableTip.PaymentMethod == PaymentMethod.Cash)
            {
                AddMinor(netCash, currencyKey, tableTip.TipMinor);
            }
        }

        var cashRefunds = refunded
            .Where(payment => payment.PaymentMethod == PaymentMethod.Cash)
            .Select(payment => new ZReportCashTenderAdjustmentReader.RefundedCashPayment(
                payment.Id,
                ResolveCurrency(payment, currencyResolver),
                ToMinor(payment.RefundedAmount ?? 0m)))
            .ToArray();
        var cashAdjustments = await ZReportCashTenderAdjustmentReader.ReadAsync(
            context, startUtc, endUtc, cashRefunds, cancellationToken);
        foreach (var adjustment in cashAdjustments)
        {
            AddMinor(netCash, adjustment.Key, adjustment.Value);
        }

        var byMethod = methodTotals
            .Select(entry => new ZReportPaymentMethodDto
            {
                PaymentMethod = entry.Key.Method.ToString(),
                Currency = NullableCurrency(entry.Key.Currency),
                TransactionCount = entry.Value.TransactionCount,
                OrderAmount = entry.Value.OrderAmount,
                TipAmount = entry.Value.TipMinor / 100m,
                TotalAmount = entry.Value.OrderAmount + entry.Value.TipMinor / 100m
            })
            .OrderByDescending(row => row.TotalAmount)
            .ThenBy(row => row.Currency, StringComparer.Ordinal)
            .ThenBy(row => row.PaymentMethod, StringComparer.Ordinal)
            .ToArray();

        return new ZReportTenderTotals(
            byMethod,
            ToCurrencyTotals(collectedTips),
            ToCurrencyTotals(refundedTips),
            ToCurrencyTotals(netCash));
    }

    private static string? ResolveCurrency(
        OrderPayment payment,
        IOrderDisplayCurrencyResolver currencyResolver) =>
        CurrencyCode.Normalize(payment.Currency)
        ?? CurrencyCode.Normalize(currencyResolver.Resolve(payment.Order));

    private static string CurrencyKey(string? currency) => currency ?? string.Empty;

    private static string? NullableCurrency(string currency) => currency.Length == 0 ? null : currency;

    private static void AddMinor(Dictionary<string, long> totals, string currency, long amountMinor)
    {
        if (amountMinor == 0) return;
        totals.TryGetValue(currency, out var current);
        totals[currency] = checked(current + amountMinor);
    }

    private static ZReportCurrencyAmountDto[] ToCurrencyTotals(
        Dictionary<string, long> totals) => totals
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .Select(entry => new ZReportCurrencyAmountDto
        {
            Currency = NullableCurrency(entry.Key),
            AmountMinor = entry.Value
        })
        .ToArray();

    private static long ToMinor(decimal amount) => decimal.ToInt64(
        decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    private sealed class TenderMethodTotal
    {
        internal int TransactionCount { get; set; }
        internal decimal OrderAmount { get; set; }
        internal long TipMinor { get; set; }
    }
}
