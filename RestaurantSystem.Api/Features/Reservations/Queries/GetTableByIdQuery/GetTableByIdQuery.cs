using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetTableByIdQuery;

public record GetTableByIdQuery(Guid TableId) : IQuery<ApiResponse<TableDto>>;

public class GetTableByIdQueryHandler : IQueryHandler<GetTableByIdQuery, ApiResponse<TableDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetTableByIdQueryHandler> _logger;

    public GetTableByIdQueryHandler(ApplicationDbContext context, ILogger<GetTableByIdQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<TableDto>> Handle(GetTableByIdQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var table = await _context.Tables
                .Where(t => t.Id == query.TableId)
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
                    QRCodeGeneratedAt = t.QRCodeGeneratedAt
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (table == null)
            {
                return ApiResponse<TableDto>.Failure("Table not found");
            }

            return ApiResponse<TableDto>.SuccessWithData(table);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting table {TableId}", query.TableId);
            return ApiResponse<TableDto>.Failure("Failed to retrieve table");
        }
    }
}
