using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;

// Date is a calendar day (no time, no timezone). The handler interprets it as
// the UTC day boundaries [Date 00:00 UTC, Date+1 00:00 UTC). Callers that want
// "today in restaurant local time" must convert before calling.
public record GetZReportQuery(DateOnly Date) : IQuery<ApiResponse<ZReportDto>>;

public class GetZReportQueryHandler : IQueryHandler<GetZReportQuery, ApiResponse<ZReportDto>>
{
    // How many top-selling items to include in the report. Promote to config
    // (e.g. ReportSettings:TopItemsCount) if this needs to vary per deployment.
    private const int TopItemsCount = 10;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetZReportQueryHandler> _logger;

    public GetZReportQueryHandler(ApplicationDbContext context, ILogger<GetZReportQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<ZReportDto>> Handle(GetZReportQuery query, CancellationToken cancellationToken)
    {
        var startOfDay = query.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var startOfNextDay = startOfDay.AddDays(1);

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
        var netSales = salesOrders.Sum(o => o.Total);
        var totalTax = salesOrders.Sum(o => o.Tax);
        var totalTips = salesOrders.Sum(o => o.Tip);
        var totalDeliveryFees = salesOrders.Sum(o => o.DeliveryFee);

        // --- Discounts ---
        var totalDiscounts = salesOrders.Sum(o => o.Discount + o.CustomerDiscountAmount + o.FidelityPointsDiscount);
        var promoCodeDiscounts = salesOrders
            .Where(o => !string.IsNullOrEmpty(o.PromoCode))
            .Sum(o => o.Discount);
        var customerDiscounts = salesOrders.Sum(o => o.CustomerDiscountAmount);
        var fidelityPointsDiscounts = salesOrders.Sum(o => o.FidelityPointsDiscount);

        // --- Refunds (from payments across all orders for the day) ---
        var refundedPayments = allOrders
            .SelectMany(o => o.Payments)
            .Where(p => p.IsRefunded && p.RefundedAmount.HasValue)
            .ToList();
        var refundCount = refundedPayments.Count;
        var totalRefundedAmount = refundedPayments.Sum(p => p.RefundedAmount ?? 0);

        // --- Cancellations ---
        var cancelledOrdersCount = cancelledOrders.Count;
        var cancelledOrdersTotal = cancelledOrders.Sum(o => o.Total);

        // --- Payment method breakdown (completed payments from sales orders) ---
        var paymentsByMethod = salesOrders
            .SelectMany(o => o.Payments)
            .Where(p => p.Status == PaymentStatus.Completed
                     || p.Status == PaymentStatus.Refunded
                     || p.Status == PaymentStatus.PartiallyRefunded)
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
            ReportDate = startOfDay,
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

        _logger.LogInformation("Generated Z-Report for {Date}: {Transactions} transactions, {NetSales} net sales",
            startOfDay.ToString("yyyy-MM-dd"), totalTransactions, netSales);

        return ApiResponse<ZReportDto>.SuccessWithData(report);
    }
}
