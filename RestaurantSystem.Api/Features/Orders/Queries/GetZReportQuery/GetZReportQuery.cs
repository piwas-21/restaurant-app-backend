using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;

// Date is a calendar day (no time, no timezone) on the RESTAURANT'S wall clock — the day the
// cashier names when they close the till. The handler converts it to the half-open instant window
// [local 00:00, local 24:00) through ITenantClock's zone (backend #372); it used to read the day as
// UTC's, which in Zurich summer is a till that closes at 02:00 and a report naming a day it does
// not cover. Stored order instants are NOT converted — they are correct as they stand.
public record GetZReportQuery(DateOnly Date) : IQuery<ApiResponse<ZReportDto>>;

public class GetZReportQueryHandler : IQueryHandler<GetZReportQuery, ApiResponse<ZReportDto>>
{
    // How many top-selling items to include in the report. Promote to config
    // (e.g. ReportSettings:TopItemsCount) if this needs to vary per deployment.
    private const int TopItemsCount = 10;

    private readonly ApplicationDbContext _context;
    private readonly ITenantClock _clock;
    private readonly ILogger<GetZReportQueryHandler> _logger;

    public GetZReportQueryHandler(
        ApplicationDbContext context,
        ITenantClock clock,
        ILogger<GetZReportQueryHandler> logger)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<ZReportDto>> Handle(GetZReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The instants the tenant's own calendar day begins and ends at. Not startOfDay.AddDays(1):
        // a local day is 23 or 25 hours on a DST changeover, and on those two days the till would
        // otherwise lose or double an hour of takings.
        var (startOfDay, startOfNextDay) = _clock.TenantDayWindowUtc(query.Date);

        // Load all orders for the day with payments and items
        var allOrders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Payments)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Where(o => !o.IsDeleted && o.OrderDate >= startOfDay && o.OrderDate < startOfNextDay)
            .ToListAsync(cancellationToken);

        // Split into non-cancelled (for sales) and cancelled
        var salesOrders = allOrders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
        var cancelledOrders = allOrders.Where(o => o.Status == OrderStatus.Cancelled).ToList();

        // --- Totals ---
        var totalTransactions = salesOrders.Count;
        var grossSales = salesOrders.Sum(o => o.SubTotal);
        var totalTips = salesOrders.Sum(o => o.Tip);

        // Net sales EXCLUDES tips. order.Total is what the customer was charged, and since S0b that
        // reliably includes the tip — but a tip is not the restaurant's revenue, so reporting it as
        // sales would overstate turnover and, under Swiss VAT, work against the separate-disclosure
        // condition that keeps a voluntary tip out of taxable consideration at all (ESTV
        // MWST-Branchen-Info 08 §8.3). TotalTips below is that separate line.
        var netSales = salesOrders.Sum(o => o.Total - o.Tip);
        var totalTax = salesOrders.Sum(o => o.Tax);
        var totalDeliveryFees = salesOrders.Sum(o => o.DeliveryFee);

        // --- Discounts ---
        var totalDiscounts = salesOrders.Sum(o => o.Discount + o.CustomerDiscountAmount + o.FidelityPointsDiscount);
        var promoCodeDiscounts = salesOrders
            .Where(o => !string.IsNullOrEmpty(o.PromoCode))
            .Sum(o => o.Discount);
        var customerDiscounts = salesOrders.Sum(o => o.CustomerDiscountAmount);
        var fidelityPointsDiscounts = salesOrders.Sum(o => o.FidelityPointsDiscount);

        // --- Refunds (from payments across all orders for the day) ---
        // Keyed on the amount given back, NOT on IsRefunded: that flag is only
        // set for a FULL refund, so filtering on it dropped every partial
        // refund out of the day's refund total.
        var refundedPayments = allOrders
            .SelectMany(o => o.Payments)
            .Where(p => p.RefundedAmount > 0)
            .ToList();
        var refundCount = refundedPayments.Count;
        var totalRefundedAmount = refundedPayments.Sum(p => p.RefundedAmount ?? 0);

        // --- Cancellations ---
        var cancelledOrdersCount = cancelledOrders.Count;
        var cancelledOrdersTotal = cancelledOrders.Sum(o => o.Total);

        // --- Payment method breakdown (captured payments from sales orders) ---
        var paymentsByMethod = salesOrders
            .SelectMany(o => o.Payments)
            .Where(p => p.Status.IsCaptured())
            .GroupBy(p => p.PaymentMethod)
            .Select(g => new ZReportPaymentMethodDto
            {
                PaymentMethod = g.Key.ToString(),
                TransactionCount = g.Count(),
                TotalAmount = g.Sum(p => p.Amount)
            })
            .OrderByDescending(p => p.TotalAmount)
            .ToList();

        // --- Sales by order type ---
        var salesByOrderType = salesOrders
            .GroupBy(o => o.Type)
            .Select(g => new ZReportOrderTypeDto
            {
                OrderType = g.Key.ToString(),
                OrderCount = g.Count(),
                TotalAmount = g.Sum(o => o.Total)
            })
            .OrderByDescending(o => o.TotalAmount)
            .ToList();

        // --- Sales by product type (root items only to avoid double-counting with child/bundle items) ---
        var salesByProductType = salesOrders
            .SelectMany(o => o.Items)
            .Where(i => i.ParentOrderItemId == null && i.Product != null)
            .GroupBy(i => i.Product!.Type)
            .Select(g => new ZReportProductTypeDto
            {
                ProductType = g.Key.ToString(),
                ItemCount = g.Sum(i => i.Quantity),
                TotalAmount = g.Sum(i => i.ItemTotal)
            })
            .OrderByDescending(p => p.TotalAmount)
            .ToList();

        // --- Top selling items (top 10 by quantity, root items only) ---
        var topSellingItems = salesOrders
            .SelectMany(o => o.Items)
            .Where(i => i.ParentOrderItemId == null)
            .GroupBy(i => i.ProductName)
            .Select(g => new ZReportTopItemDto
            {
                ProductName = g.Key,
                QuantitySold = g.Sum(i => i.Quantity),
                TotalRevenue = g.Sum(i => i.ItemTotal)
            })
            .OrderByDescending(i => i.QuantitySold)
            .Take(TopItemsCount)
            .ToList();

        var report = new ZReportDto
        {
            // The calendar day the report is FOR, not the instant it starts at: the cashier UI
            // renders this as a date, and the tenant-day start (22:00Z the evening before, in
            // Zurich summer) would print as the previous day in any browser at or west of UTC.
            ReportDate = query.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            GeneratedAt = DateTime.UtcNow,
            TotalTransactions = totalTransactions,
            GrossSales = grossSales,
            NetSales = netSales,
            TotalTax = totalTax,
            TotalTips = totalTips,
            TotalDeliveryFees = totalDeliveryFees,
            Discounts = new ZReportDiscountsDto
            {
                TotalDiscounts = totalDiscounts,
                PromoCodeDiscounts = promoCodeDiscounts,
                CustomerDiscounts = customerDiscounts,
                FidelityPointsDiscounts = fidelityPointsDiscounts
            },
            Refunds = new ZReportRefundsDto
            {
                RefundCount = refundCount,
                TotalRefundedAmount = totalRefundedAmount
            },
            CancelledOrdersCount = cancelledOrdersCount,
            CancelledOrdersTotal = cancelledOrdersTotal,
            PaymentsByMethod = paymentsByMethod,
            SalesByOrderType = salesByOrderType,
            SalesByProductType = salesByProductType,
            TopSellingItems = topSellingItems
        };

        // The window is logged beside the day because they are no longer the same statement, and
        // comparing the two is how an operator's "this report looks wrong" gets answered.
        _logger.LogInformation(
            "Generated Z-Report for {Date} ({ZoneId}, [{StartUtc:o}, {EndUtc:o})): {Transactions} transactions, {NetSales} net sales",
            query.Date,
            _clock.TimeZone.Id,
            startOfDay,
            startOfNextDay,
            totalTransactions,
            netSales);

        return ApiResponse<ZReportDto>.SuccessWithData(report);
    }
}
