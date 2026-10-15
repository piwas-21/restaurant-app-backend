using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetTablesQuery;

public record GetTablesQuery(
    bool? IsActive = null,
    bool? IsOutdoor = null,
    bool IncludeOccupancy = false
) : IQuery<ApiResponse<List<TableDto>>>;

public partial class GetTablesQueryHandler : IQueryHandler<GetTablesQuery, ApiResponse<List<TableDto>>>
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

            // Public table lists omit the live customer and order data in this staff projection.
            var activeOrders = query.IncludeOccupancy
                ? await ReadActiveOrdersByTableAsync(cancellationToken)
                : ActiveTableOrderProjection.Empty;

            var tables = await tablesQuery
                .OrderBy(t => t.TableNumber)
                .Select(t => new TableDto
                {
                    Id = t.Id,
                    TableNumber = t.TableNumber,
                    MaxGuests = t.MaxGuests,
                    IsActive = t.IsActive,
                    IsOutdoor = t.IsOutdoor,
                    ReadinessState = query.IncludeOccupancy ? t.ReadinessState.ToString() : null,
                    ReadinessVersion = query.IncludeOccupancy ? t.ReadinessVersion : null,
                    PositionX = t.PositionX,
                    PositionY = t.PositionY,
                    Width = t.Width,
                    Height = t.Height,
                    Shape = t.Shape,
                    Rotation = t.Rotation,
                    Notes = t.Notes,
                    QRCodeData = t.QRCodeData,
                    QRCodeGeneratedAt = t.QRCodeGeneratedAt,
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
                activeOrders.ApplyTo(tables);
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
