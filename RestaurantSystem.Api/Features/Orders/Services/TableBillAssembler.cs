using System.Globalization;
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
    private readonly IOrderPermittedActionsService? _permittedActions;

    public TableBillAssembler(
        ApplicationDbContext context,
        IOrderMappingService mappingService,
        ILogger<TableBillAssembler> logger,
        ITableBillTargetResolver? targetResolver = null,
        IOrderPermittedActionsService? permittedActions = null)
    {
        _context = context;
        _mappingService = mappingService;
        _logger = logger;
        _targetResolver = targetResolver ?? new TableBillTargetResolver(context);
        _permittedActions = permittedActions;
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
            .Include(value => value.Table)
            .SingleOrDefaultAsync(value => value.Id == serviceSessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var orders = await QueryOrders(o => o.ServiceSessionId == serviceSessionId
            && o.Status != OrderStatus.Cancelled, cancellationToken);
        return BuildBill(
            orders, session.TableNumber, session.TableId, session.Table?.TableNumber,
            session.Id, session.Version, session.Currency);
    }

    /// <summary>
    /// Assembles all supplied visits from one batched member-order graph load. The caller owns the
    /// session metadata read, so an active-session list never re-reads one session per bill.
    /// </summary>
    public async Task<IReadOnlyList<TableBillDto?>> AssembleManyAsync(
        IReadOnlyList<TableServiceSession> sessions, CancellationToken cancellationToken)
    {
        if (sessions.Count == 0)
        {
            return Array.Empty<TableBillDto?>();
        }

        var sessionIds = sessions.Select(session => session.Id).ToArray();
        var orders = await QueryOrders(order => order.ServiceSessionId.HasValue
            && sessionIds.Contains(order.ServiceSessionId.Value)
            && order.Status != OrderStatus.Cancelled, cancellationToken);
        var ordersBySession = orders
            .Where(order => order.ServiceSessionId.HasValue)
            .GroupBy(order => order.ServiceSessionId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        return sessions.Select(session => ordersBySession.TryGetValue(session.Id, out var members)
                ? BuildBill(
                    members, session.TableNumber, session.TableId, session.Table?.TableNumber,
                    session.Id, session.Version, session.Currency)
                : null)
            .ToList();
    }

    private async Task<TableBillDto?> AssembleLegacyUnassignedAsync(
        int tableNumber, CancellationToken cancellationToken)
    {
        // Legacy table-number reads must follow the same authoritative settlement predicate as
        // writes: a completed-but-unpaid round is still billable, while cancelled/refunded rows
        // are not. This keeps GET and POST from disagreeing about the same legacy visit.
        var orders = await QueryOrders(o => o.TableNumber == tableNumber
            && o.ServiceSessionId == null, cancellationToken,
            OrderSettlementEligibility.OperationalQueuePredicate());
        return BuildBill(orders, tableNumber, null, null, null, null, null);
    }

    private async Task<List<Order>> QueryOrders(
        System.Linq.Expressions.Expression<Func<Order, bool>> predicate,
        CancellationToken cancellationToken,
        System.Linq.Expressions.Expression<Func<Order, bool>>? settlementPredicate = null)
    {
        var query = _context.Orders
            .IncludeOrderLineGraph()
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Where(o => !o.IsDeleted && o.Type == OrderType.DineIn)
            .Where(predicate);
        if (settlementPredicate is not null)
        {
            query = query.Where(settlementPredicate);
        }

        return await query
            .OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    private TableBillDto? BuildBill(
        List<Order> orders,
        int? tableNumber,
        Guid? tableId,
        string? tableLabel,
        Guid? serviceSessionId,
        int? serviceSessionVersion,
        string? currency)
    {
        if (orders.Count == 0)
        {
            return null;
        }

        var bill = new TableBillDto
        {
            TableNumber = tableNumber,
            TableId = tableId,
            TableLabel = tableLabel ?? tableNumber?.ToString(CultureInfo.InvariantCulture),
            ServiceSessionId = serviceSessionId,
            ServiceSessionVersion = serviceSessionVersion,
            Currency = CurrencyCode.Normalize(currency),
            GeneratedAt = DateTime.UtcNow,
            OrderCount = orders.Count,
        };

        foreach (var order in orders)
        {
            // QueryOrders eagerly loads every navigation MapToOrderDto reads. Keeping this path
            // synchronous makes a bill's cost independent of its number of member orders.
            var dto = _mappingService.MapToOrderDto(order);
            var canCollect = OrderSettlementEligibility.CanCollect(order);
            var round = new TableBillRoundDto
            {
                Order = dto,
                SettlementState = SettlementStateFor(order, canCollect),
                Outstanding = OrderSettlementEligibility.Outstanding(order),
                RefundedAmount = OrderSettlementEligibility.RefundedAmount(order),
                Credit = OrderSettlementEligibility.Credit(order),
                CanCollect = canCollect,
                PermittedActions = _permittedActions?.GetPermittedActions(order)
                    ?? Array.Empty<OrderPermittedActionDto>()
            };
            bill.Orders.Add(dto);
            bill.Rounds.Add(round);
            bill.EligibleOutstanding += canCollect ? round.Outstanding : 0m;
            bill.Credit += round.Credit;
        }

        Summarize(bill);
        _logger.LogInformation(
            "Assembled bill for table {TableNumber}, session {ServiceSessionId}: {OrderCount} orders, remaining {Remaining}",
            tableNumber, serviceSessionId, bill.OrderCount, bill.Remaining);
        return bill;
    }

    private static string SettlementStateFor(Order order, bool canCollect)
    {
        var refunded = OrderSettlementEligibility.RefundedAmount(order);
        var captured = order.Payments.Where(payment => payment.Status.IsCaptured())
            .Sum(payment => payment.Amount);
        if (order.Status == OrderStatus.Refunded
            || order.PaymentStatus == PaymentStatus.Refunded
            || (captured > 0m && refunded >= captured))
        {
            return "Refunded";
        }

        if (refunded > 0m || order.PaymentStatus == PaymentStatus.PartiallyRefunded)
        {
            return "PartiallyRefunded";
        }

        if (OrderSettlementEligibility.Credit(order) > 0m
            || order.PaymentStatus == PaymentStatus.Overpaid)
        {
            return "Credit";
        }

        return canCollect ? "EligibleDebt" : "Settled";
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
