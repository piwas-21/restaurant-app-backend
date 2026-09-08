using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class TableBillAssembler : ITableBillAssembler
{
    /// <summary>
    /// The bill's members. "Open" mirrors <see cref="Commands.CompleteAllTableOrdersCommand"/>:
    /// everything that is not Completed and not Cancelled is still this table's service —
    /// including an order that is already fully paid, which renders as a settled line with zero
    /// outstanding. There is deliberately NO date filter: a session is closed by completing the
    /// table, not by midnight. An order left open yesterday is still owed today.
    /// </summary>
    /// <remarks>Public so the bill-payment command filters on the SAME definition — these two
    /// lists must never drift.</remarks>
    public static readonly OrderStatus[] ExcludedStatuses = { OrderStatus.Completed, OrderStatus.Cancelled };

    private readonly ApplicationDbContext _context;
    private readonly IOrderMappingService _mappingService;
    private readonly ILogger<TableBillAssembler> _logger;

    public TableBillAssembler(
        ApplicationDbContext context,
        IOrderMappingService mappingService,
        ILogger<TableBillAssembler> logger)
    {
        _context = context;
        _mappingService = mappingService;
        _logger = logger;
    }

    public async Task<TableBillDto?> AssembleAsync(int tableNumber, CancellationToken cancellationToken)
    {
        var orders = await _context.Orders
            .IncludeOrderLineGraph()
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Where(o => !o.IsDeleted
                && o.Type == OrderType.DineIn
                && o.TableNumber == tableNumber
                && !ExcludedStatuses.Contains(o.Status))
            // Oldest round first: the bill reads in the order the table ordered.
            .OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber)
            // Idempotent with the flag inside IncludeOrderLineGraph, but kept explicit at this
            // level: Payments and StatusHistory are sibling collections on the same query (S8733).
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            return null;
        }

        var bill = new TableBillDto
        {
            TableNumber = tableNumber,
            GeneratedAt = DateTime.UtcNow,
            OrderCount = orders.Count,
        };

        foreach (var order in orders)
        {
            bill.Orders.Add(await _mappingService.MapToOrderDtoAsync(order, cancellationToken));
        }

        Summarize(bill);

        _logger.LogInformation(
            "Assembled bill for table {TableNumber}: {OrderCount} open orders, total {Total}, remaining {Remaining}",
            tableNumber, bill.OrderCount, bill.Total, bill.Remaining);

        return bill;
    }

    /// <summary>Rolls the per-order figures up into the bill-level sums.</summary>
    private static void Summarize(TableBillDto bill)
    {
        bill.SubTotal = bill.Orders.Sum(o => o.SubTotal);
        bill.Tax = bill.Orders.Sum(o => o.Tax);
        bill.Discount = bill.Orders.Sum(o => o.Discount);
        bill.Tip = bill.Orders.Sum(o => o.Tip);
        bill.Total = bill.Orders.Sum(o => o.Total);
        bill.TotalPaid = bill.Orders.Sum(o => o.TotalPaid);
        // Clamp per order: an overpaid order must not offset a sibling's outstanding balance.
        bill.Remaining = bill.Orders.Sum(o => Math.Max(0, o.RemainingAmount));
    }
}
