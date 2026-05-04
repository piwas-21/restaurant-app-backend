using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetTablesQuery;

public record GetTablesQuery(
    bool? IsActive = null,
    bool? IsOutdoor = null
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

            // Define active order statuses (orders still at table)
            var activeOrderStatuses = new[]
            {
                OrderStatus.Pending,
                OrderStatus.Confirmed,
                OrderStatus.Preparing,
                OrderStatus.Ready,
                OrderStatus.PendingApproval
            };

            // Get active dine-in orders grouped by table number
            var activeOrdersByTable = await _context.Orders
                .Where(o => o.TableNumber != null
                    && o.Type == OrderType.DineIn
                    && activeOrderStatuses.Contains(o.Status)
                    && !o.IsDeleted)
                .GroupBy(o => o.TableNumber!.Value)
                .Select(g => new
                {
                    TableNumber = g.Key.ToString(),
                    OrderCount = g.Count(),
                    Occupants = g.Select(o => new TableOccupantDto
                    {
                        CustomerName = o.CustomerName,
                        OrderNumber = o.OrderNumber,
                        OrderDate = o.OrderDate,
                        IsLoggedInUser = o.UserId != null
                    }).ToList()
                })
                .ToDictionaryAsync(x => x.TableNumber, cancellationToken);

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

            // Populate order-based occupancy for each table
            foreach (var table in tables)
            {
                if (activeOrdersByTable.TryGetValue(table.TableNumber, out var orderInfo))
                {
                    table.IsOccupied = true;
                    table.ActiveOrderCount = orderInfo.OrderCount;
                    table.Occupants = orderInfo.Occupants;
                }
                else
                {
                    table.IsOccupied = false;
                    table.ActiveOrderCount = 0;
                    table.Occupants = null;
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
}
