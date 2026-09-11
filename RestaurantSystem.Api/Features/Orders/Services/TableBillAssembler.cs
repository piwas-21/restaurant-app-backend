using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class TableBillAssembler : ITableBillAssembler
{
    /// <summary>Legacy table bills consider non-terminal orders open.</summary>
    public static readonly OrderStatus[] ExcludedStatuses = { OrderStatus.Completed, OrderStatus.Cancelled };

    private readonly ApplicationDbContext _context;
    private readonly IOrderMappingService _mappingService;
    private readonly ILogger<TableBillAssembler> _logger;
    private readonly ITableBillTargetResolver _targetResolver;

    public TableBillAssembler(
        ApplicationDbContext context,
        IOrderMappingService mappingService,
        ILogger<TableBillAssembler> logger,
        ITableBillTargetResolver? targetResolver = null)
    {
        _context = context;
        _mappingService = mappingService;
        _logger = logger;
        _targetResolver = targetResolver ?? new TableBillTargetResolver(context);
    }

    /// <summary>
    /// Preserves the #508/#529 table-number contract. A mixed legacy/explicit target returns a
    /// structured ambiguity marker instead of combining rounds from unrelated visits.
    /// </summary>
    public async Task<TableBillDto?> AssembleAsync(int tableNumber, CancellationToken cancellationToken)
    {
        var target = await _targetResolver.ResolveAsync(tableNumber, cancellationToken);
        if (target.IsAmbiguous)
        {
            return new TableBillDto
            {
                TableNumber = tableNumber,
                GeneratedAt = DateTime.UtcNow,
                IsAmbiguous = true
            };
        }

        return target.ServiceSessionId.HasValue
            ? await AssembleAsync(target.ServiceSessionId.Value, cancellationToken)
            : await AssembleLegacyUnassignedAsync(tableNumber, cancellationToken);
    }

    /// <summary>Assembles the durable bill for one explicit visit, including settled rounds.</summary>
    public async Task<TableBillDto?> AssembleAsync(Guid serviceSessionId, CancellationToken cancellationToken)
    {
        var session = await _context.TableServiceSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == serviceSessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var orders = await QueryOrders(o => o.ServiceSessionId == serviceSessionId
            && o.Status != OrderStatus.Cancelled, cancellationToken);
        return await BuildBillAsync(orders, session.TableNumber, session.Id, session.Version, session.Currency,
            cancellationToken);
    }

    private async Task<TableBillDto?> AssembleLegacyUnassignedAsync(
        int tableNumber, CancellationToken cancellationToken)
    {
        var orders = await QueryOrders(o => o.TableNumber == tableNumber
            && o.ServiceSessionId == null
            && ExcludedStatuses.Contains(o.Status), cancellationToken);
        return await BuildBillAsync(orders, tableNumber, null, null, null, cancellationToken);
    }

    private async Task<List<Order>> QueryOrders(
        System.Linq.Expressions.Expression<Func<Order, bool>> predicate,
        CancellationToken cancellationToken)
    {
        return await _context.Orders
            .IncludeOrderLineGraph()
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Where(o => !o.IsDeleted && o.Type == OrderType.DineIn)
            .Where(predicate)
            .OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    private async Task<TableBillDto?> BuildBillAsync(
        List<Order> orders,
        int tableNumber,
        Guid? serviceSessionId,
        int? serviceSessionVersion,
        string? currency,
        CancellationToken cancellationToken)
    {
        if (orders.Count == 0)
        {
            return null;
        }

        var bill = new TableBillDto
        {
            TableNumber = tableNumber,
            ServiceSessionId = serviceSessionId,
            ServiceSessionVersion = serviceSessionVersion,
            Currency = CurrencyCode.Normalize(currency),
            GeneratedAt = DateTime.UtcNow,
            OrderCount = orders.Count,
        };

        foreach (var order in orders)
        {
            bill.Orders.Add(await _mappingService.MapToOrderDtoAsync(order, cancellationToken));
        }

        Summarize(bill);
        _logger.LogInformation(
            "Assembled bill for table {TableNumber}, session {ServiceSessionId}: {OrderCount} orders, remaining {Remaining}",
            tableNumber, serviceSessionId, bill.OrderCount, bill.Remaining);
        return bill;
    }

    private static void Summarize(TableBillDto bill)
    {
        bill.SubTotal = bill.Orders.Sum(o => o.SubTotal);
        bill.Tax = bill.Orders.Sum(o => o.Tax);
        bill.Discount = bill.Orders.Sum(o => o.Discount);
        bill.Tip = bill.Orders.Sum(o => o.Tip);
        bill.Total = bill.Orders.Sum(o => o.Total);
        bill.TotalPaid = bill.Orders.Sum(o => o.TotalPaid);
        bill.Remaining = bill.Orders.Sum(o => Math.Max(0, o.RemainingAmount));
    }
}
