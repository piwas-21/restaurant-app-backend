using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetTablesQuery;

public record GetTablesQuery(
    bool? IsActive = null,
    bool? IsOutdoor = null,
    bool IncludeOccupancy = false
) : IQuery<ApiResponse<List<TableDto>>>;

public class GetTablesQueryHandler : IQueryHandler<GetTablesQuery, ApiResponse<List<TableDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetTablesQueryHandler> _logger;

    public GetTablesQueryHandler(ApplicationDbContext context, ILogger<GetTablesQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<List<TableDto>>> Handle(GetTablesQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var tablesQuery = _context.Tables.AsQueryable();

            if (query.IsActive.HasValue)
            {
                tablesQuery = tablesQuery.Where(t => t.IsActive == query.IsActive.Value);
            }

            if (query.IsOutdoor.HasValue)
            {
                tablesQuery = tablesQuery.Where(t => t.IsOutdoor == query.IsOutdoor.Value);
            }

            var now = DateTime.UtcNow;

            // Occupancy is a staff-only projection. The public availability route still returns
            // the table catalogue and reservation status, but never live customer/order data.
            var activeOrdersByTable = query.IncludeOccupancy
                ? await ReadActiveOrdersByTableAsync(cancellationToken)
                : new Dictionary<string, ActiveTableOrderInfo>();

            var tables = await tablesQuery
                .OrderBy(t => t.TableNumber)
                .Select(t => new TableDto
                {
                    Id = t.Id,
                    TableNumber = t.TableNumber,
                    MaxGuests = t.MaxGuests,
                    IsActive = t.IsActive,
                    IsOutdoor = t.IsOutdoor,
                    PositionX = t.PositionX,
                    PositionY = t.PositionY,
                    Width = t.Width,
                    Height = t.Height,
                    Shape = t.Shape,
                    Rotation = t.Rotation,
                    Notes = t.Notes,
                    QRCodeData = t.QRCodeData,
                    QRCodeGeneratedAt = t.QRCodeGeneratedAt,
                    // Check if table has active reservation
                    IsReserved = _context.TableReservations.Any(r =>
                        r.TableId == t.Id &&
                        r.IsActive &&
                        r.ReservedUntil > now),
                    ReservedUntil = _context.TableReservations
                        .Where(r => r.TableId == t.Id && r.IsActive && r.ReservedUntil > now)
                        .OrderByDescending(r => r.ReservedUntil)
                        .Select(r => r.ReservedUntil)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            if (query.IncludeOccupancy)
            {
                foreach (var table in tables)
                {
                    if (activeOrdersByTable.TryGetValue(table.TableNumber, out var orderInfo))
                    {
                        table.IsOccupied = true;
                        table.ActiveOrderCount = orderInfo.OrderCount;
                        table.Occupants = orderInfo.Occupants;
                    }
                }
            }

            return ApiResponse<List<TableDto>>.SuccessWithData(tables);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tables");
            return ApiResponse<List<TableDto>>.Failure("Failed to retrieve tables");
        }
    }

    private async Task<Dictionary<string, ActiveTableOrderInfo>> ReadActiveOrdersByTableAsync(
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
        var allDineInOrders = _context.Orders
            .AsNoTracking()
            .Where(order => order.TableNumber != null
                && order.Type == OrderType.DineIn
                && !order.IsDeleted);
        var liveOrders = allDineInOrders.Where(order => activeOrderStatuses.Contains(order.Status));
        var completedUnpaidOrders = allDineInOrders
            .Where(order => order.Status == OrderStatus.Completed)
            .Where(OrderSettlementEligibility.CanCollectQuery());
        var eligibleOrders = liveOrders.Concat(completedUnpaidOrders);

        // Keep the exact occupancy count without materializing every occupant row. The second
        // query returns one recent representative per table so the staff shape remains useful
        // without turning a busy table into an unbounded PII payload.
        var countRows = await eligibleOrders
            .GroupBy(order => order.TableNumber!.Value)
            .Select(group => new
            {
                TableNumber = group.Key,
                OrderCount = group.Count()
            })
            .ToListAsync(cancellationToken);
        var latestRows = await eligibleOrders
            .GroupBy(order => order.TableNumber!.Value)
            .Select(group => group
                .OrderByDescending(order => order.OrderDate)
                .ThenBy(order => order.Id)
                .Select(order => new
                {
                    TableNumber = order.TableNumber!.Value,
                    CustomerName = order.CustomerName,
                    OrderNumber = order.OrderNumber,
                    OrderDate = order.OrderDate,
                    IsLoggedInUser = order.UserId != null
                })
                .First())
            .ToListAsync(cancellationToken);
        var latestByTable = latestRows.ToDictionary(
            row => row.TableNumber,
            row => new TableOccupantDto
            {
                CustomerName = row.CustomerName,
                OrderNumber = row.OrderNumber,
                OrderDate = row.OrderDate,
                IsLoggedInUser = row.IsLoggedInUser
            });

        var openSessionTables = await _context.TableServiceSessions
            .AsNoTracking()
            .Where(session => session.Status == TableServiceSessionStatus.Open)
            .Select(session => session.TableNumber.ToString())
            .ToListAsync(cancellationToken);
        var result = countRows.ToDictionary(
            row => row.TableNumber.ToString(),
            row => new ActiveTableOrderInfo
            {
                TableNumber = row.TableNumber.ToString(),
                OrderCount = row.OrderCount,
                Occupants = latestByTable.TryGetValue(row.TableNumber, out var occupant)
                    ? [occupant]
                    : []
            });

        foreach (var tableNumber in openSessionTables)
        {
            result.TryAdd(tableNumber, new ActiveTableOrderInfo
            {
                TableNumber = tableNumber,
                Occupants = []
            });
        }

        return result;
    }

    private sealed class ActiveTableOrderInfo
    {
        public string TableNumber { get; init; } = string.Empty;
        public int OrderCount { get; init; }
        public List<TableOccupantDto> Occupants { get; init; } = [];
    }
}
