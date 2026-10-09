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
        var payments = await ReadPaymentsAsync(context, startUtc, endUtc, cancellationToken);

        var tableTips = await ZReportTableAccountTipReader.ReadAsync(
            context, startUtc, endUtc, cancellationToken);
        var captured = payments.Where(payment => IsCapturedInWindow(payment, startUtc, endUtc)).ToArray();
        var refunded = payments.Where(payment => IsRefundedInWindow(payment, startUtc, endUtc)).ToArray();

        var methodTotals = new Dictionary<(PaymentMethod Method, string Currency), TenderMethodTotal>();
        var collectedTips = new Dictionary<string, long>(StringComparer.Ordinal);
        var refundedTips = new Dictionary<string, long>(StringComparer.Ordinal);
        var netCash = new Dictionary<string, long>(StringComparer.Ordinal);

        AccumulateCapturedPayments(captured, currencyResolver, methodTotals, collectedTips, netCash);
        AccumulateRefundedPayments(refunded, currencyResolver, refundedTips, netCash);
        AccumulateTableTips(tableTips, methodTotals, collectedTips, netCash);
        await AddCashRefundAdjustmentsAsync(
            context, startUtc, endUtc, refunded, currencyResolver, netCash, cancellationToken);

        var byMethod = BuildMethodTotals(methodTotals);

        return new ZReportTenderTotals(
            byMethod,
            ToCurrencyTotals(collectedTips),
            ToCurrencyTotals(refundedTips),
            ToCurrencyTotals(netCash));
    }

    private static async Task<OrderPayment[]> ReadPaymentsAsync(
        ApplicationDbContext context,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken) =>
        await context.OrderPayments.AsNoTracking()
            .Include(payment => payment.Order.ExternalReference)
            .Where(payment => !payment.Order.IsDeleted
                && (payment.Status == PaymentStatus.Completed
                    || payment.Status == PaymentStatus.PartiallyRefunded
                    || payment.Status == PaymentStatus.Refunded)
                && ((payment.PaymentDate >= startUtc && payment.PaymentDate < endUtc)
                    || (payment.RefundDate >= startUtc && payment.RefundDate < endUtc)))
            .ToArrayAsync(cancellationToken);

    private static bool IsCapturedInWindow(OrderPayment payment, DateTime startUtc, DateTime endUtc) =>
        payment.PaymentDate >= startUtc && payment.PaymentDate < endUtc && payment.Status.IsCaptured();

    private static bool IsRefundedInWindow(OrderPayment payment, DateTime startUtc, DateTime endUtc) =>
        payment.RefundDate >= startUtc && payment.RefundDate < endUtc;

    private static void AccumulateCapturedPayments(
        IEnumerable<OrderPayment> captured,
        IOrderDisplayCurrencyResolver currencyResolver,
        Dictionary<(PaymentMethod Method, string Currency), TenderMethodTotal> methodTotals,
        Dictionary<string, long> collectedTips,
        Dictionary<string, long> netCash)
    {
        foreach (var payment in captured)
        {
            var currencyKey = CurrencyKey(ResolveCurrency(payment, currencyResolver));
            if (payment.Order.Status != OrderStatus.Cancelled)
                AddCapturedMethodTotal(methodTotals, payment, currencyKey);

            // The sales-by-method breakdown keeps its established cancelled-order exclusion,
            // while movement and gratuity totals below follow the money actually taken.
            AddMinor(collectedTips, currencyKey, payment.TipMinor);
            if (payment.PaymentMethod == PaymentMethod.Cash)
                AddMinor(netCash, currencyKey, ToMinor(payment.Amount) + payment.TipMinor);
        }
    }

    private static void AddCapturedMethodTotal(
        Dictionary<(PaymentMethod Method, string Currency), TenderMethodTotal> methodTotals,
        OrderPayment payment,
        string currencyKey)
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

    private static void AccumulateRefundedPayments(
        IEnumerable<OrderPayment> refunded,
        IOrderDisplayCurrencyResolver currencyResolver,
        Dictionary<string, long> refundedTips,
        Dictionary<string, long> netCash)
    {
        foreach (var payment in refunded)
        {
            var currencyKey = CurrencyKey(ResolveCurrency(payment, currencyResolver));
            AddMinor(refundedTips, currencyKey, payment.RefundedTipMinor);
            if (payment.PaymentMethod == PaymentMethod.Cash)
                AddMinor(netCash, currencyKey,
                    -(ToMinor(payment.RefundedAmount ?? 0m) + payment.RefundedTipMinor));
        }
    }

    private static void AccumulateTableTips(
        IEnumerable<ZReportTableAccountTipDto> tableTips,
        Dictionary<(PaymentMethod Method, string Currency), TenderMethodTotal> methodTotals,
        Dictionary<string, long> collectedTips,
        Dictionary<string, long> netCash)
    {
        foreach (var tableTip in tableTips)
        {
            var currencyKey = CurrencyKey(CurrencyCode.Normalize(tableTip.Currency));
            AddTableTipMethodTotal(methodTotals, tableTip, currencyKey);
            AddMinor(collectedTips, currencyKey, tableTip.TipMinor);
            if (tableTip.PaymentMethod == PaymentMethod.Cash)
                AddMinor(netCash, currencyKey, tableTip.TipMinor);
        }
    }

    private static void AddTableTipMethodTotal(
        Dictionary<(PaymentMethod Method, string Currency), TenderMethodTotal> methodTotals,
        ZReportTableAccountTipDto tableTip,
        string currencyKey)
    {
        var key = (tableTip.PaymentMethod, currencyKey);
        if (!methodTotals.TryGetValue(key, out var total))
        {
            // Table/account tip rows supplement the allocated OrderPayment amount. They are
            // not additional tenders and therefore must not increase TransactionCount.
            total = new TenderMethodTotal();
            methodTotals.Add(key, total);
        }

        total.TipMinor = checked(total.TipMinor + tableTip.TipMinor);
    }

    private static async Task AddCashRefundAdjustmentsAsync(
        ApplicationDbContext context,
        DateTime startUtc,
        DateTime endUtc,
        IEnumerable<OrderPayment> refunded,
        IOrderDisplayCurrencyResolver currencyResolver,
        Dictionary<string, long> netCash,
        CancellationToken cancellationToken)
    {
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
            AddMinor(netCash, adjustment.Key, adjustment.Value);
    }

    private static ZReportPaymentMethodDto[] BuildMethodTotals(
        Dictionary<(PaymentMethod Method, string Currency), TenderMethodTotal> methodTotals) =>
        methodTotals
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
