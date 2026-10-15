using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetTablesQuery;

public partial class GetTablesQueryHandler
{
    private async Task<ActiveTableOrderProjection> ReadActiveOrdersByTableAsync(
        CancellationToken cancellationToken)
    {
        var activeOrderStatuses = new[]
        {
            OrderStatus.Pending,
            OrderStatus.Confirmed,
            OrderStatus.Preparing,
            OrderStatus.Ready,
            OrderStatus.PendingApproval
        };
        var allDineInOrders = TableServiceSessionCloseRules.ExcludeArchivedLegacyOccupancy(
            _context.Orders
            .AsNoTracking()
            .Where(order => order.Type == OrderType.DineIn && !order.IsDeleted
                && (order.TableId != null || order.TableNumber != null))
            .Where(order => !order.ServiceSessionId.HasValue
                || !_context.TableServiceSessions.Any(session => session.Id == order.ServiceSessionId.Value
                    && session.ReleasedAt != null)),
            _context.TableOccupancyRecoveryDispositions);
        var liveOrders = allDineInOrders.Where(order => activeOrderStatuses.Contains(order.Status));
        var completedUnpaidOrders = allDineInOrders
            .Where(order => order.Status == OrderStatus.Completed)
            .Where(OrderSettlementEligibility.CanCollectQuery());
        var eligibleOrders = liveOrders.Concat(completedUnpaidOrders);

        // Keep the exact occupancy count without materializing every occupant row. The second
        // query returns one recent representative per table so the staff shape remains useful
        // without turning a busy table into an unbounded PII payload.
        var countRows = await eligibleOrders
            .GroupBy(order => new
            {
                order.TableId,
                TableNumber = order.TableId.HasValue ? (int?)null : order.TableNumber
            })
            .Select(group => new
            {
                group.Key.TableId,
                group.Key.TableNumber,
                OrderCount = group.Count()
            })
            .ToListAsync(cancellationToken);
        var latestRows = await eligibleOrders
            .GroupBy(order => new
            {
                order.TableId,
                TableNumber = order.TableId.HasValue ? (int?)null : order.TableNumber
            })
            .Select(group => group
                .OrderByDescending(order => order.OrderDate)
                .ThenBy(order => order.Id)
                .Select(order => new
                {
                    group.Key.TableId,
                    group.Key.TableNumber,
                    CustomerName = order.CustomerName,
                    OrderNumber = order.OrderNumber,
                    DailyOrderNumber = order.DailyOrderNumber,
                    BusinessDate = order.BusinessDate,
                    OrderDate = order.OrderDate,
                    IsLoggedInUser = order.UserId != null
                })
                .First())
            .ToListAsync(cancellationToken);
        var latestByTable = latestRows.ToDictionary(
            row => new TableOrderKey(row.TableId, row.TableNumber),
            row => new TableOccupantDto
            {
                CustomerName = row.CustomerName,
                OrderNumber = row.OrderNumber,
                DailyOrderNumber = row.DailyOrderNumber,
                BusinessDate = row.BusinessDate,
                OrderDate = row.OrderDate,
                IsLoggedInUser = row.IsLoggedInUser
            });

        var openSessionTables = await _context.TableServiceSessions
            .AsNoTracking()
            .Where(session => session.Status == TableServiceSessionStatus.Open
                && session.ReleasedAt == null)
            .Select(session => new
            {
                session.TableId,
                TableNumber = session.TableId.HasValue ? (int?)null : session.TableNumber
            })
            .Where(session => session.TableId.HasValue || session.TableNumber.HasValue)
            .ToListAsync(cancellationToken);
        var result = countRows.ToDictionary(
            row => new TableOrderKey(row.TableId, row.TableNumber),
            row => new ActiveTableOrderInfo
            {
                OrderCount = row.OrderCount,
                Occupants = latestByTable.TryGetValue(
                    new TableOrderKey(row.TableId, row.TableNumber), out var occupant)
                    ? [occupant]
                    : []
            });
        foreach (var session in openSessionTables)
        {
            result.TryAdd(new TableOrderKey(session.TableId, session.TableNumber),
                new ActiveTableOrderInfo());
        }

        return new ActiveTableOrderProjection(result);
    }
}
