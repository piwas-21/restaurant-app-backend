using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Infrastructure.Persistence.Support;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CreateTableCommand;

public record CreateTableCommand(CreateTableDto TableData) : ICommand<ApiResponse<TableDto>>;

public class CreateTableCommandHandler : ICommandHandler<CreateTableCommand, ApiResponse<TableDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CreateTableCommandHandler> _logger;

    public CreateTableCommandHandler(ApplicationDbContext context, ILogger<CreateTableCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<TableDto>> Handle(CreateTableCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var data = command.TableData;

            // Check if table number already exists
            var existingTable = await _context.Tables
                .FirstOrDefaultAsync(t => t.TableNumber == data.TableNumber, cancellationToken);

            if (existingTable != null)
            {
                return ApiResponse<TableDto>.Failure($"Table with number '{data.TableNumber}' already exists");
            }

            // Auto-link the new table to the plan the guest map renders — the
            // same default-plan selection as FloorPlanService — and coerce its
            // geometry into metres. The still-deployed pixel-canvas frontend
            // posts pixel-scale size/position that must not reach the guest map
            // as-is (FLOOR-PLAN-REVAMP §5.2/§6, prod-first).
            var plan = await _context.FloorPlans
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.DisplayOrder)
                .Select(p => new { p.Id, p.WidthMeters, p.HeightMeters })
                .FirstOrDefaultAsync(cancellationToken);

            var planWidth = plan?.WidthMeters ?? FloorPlanMigrationSql.RoomWidthMeters;
            var planHeight = plan?.HeightMeters ?? FloorPlanMigrationSql.RoomHeightMeters;

            var (width, height) = TableGeometryDefaults.MetreFootprint(
                data.Width, data.Height, data.MaxGuests, planWidth, planHeight);
            var (positionX, positionY) = TableGeometryDefaults.MetrePosition(
                data.PositionX, data.PositionY, planWidth, planHeight);

            var table = new Table
            {
                TableNumber = data.TableNumber,
                MaxGuests = data.MaxGuests,
                IsActive = data.IsActive,
                IsOutdoor = data.IsOutdoor,
                FloorPlanId = plan?.Id,
                PositionX = positionX,
                PositionY = positionY,
                Width = width,
                Height = height,
                Shape = TableGeometryDefaults.NormalizeShape(data.Shape),
                Rotation = data.Rotation,
                Notes = data.Notes,
                CreatedBy = "System" // TODO: Get from current user
            };

            _context.Tables.Add(table);
            await _context.SaveChangesAsync(cancellationToken);

            var tableDto = new TableDto
            {
                Id = table.Id,
                TableNumber = table.TableNumber,
                MaxGuests = table.MaxGuests,
                IsActive = table.IsActive,
                IsOutdoor = table.IsOutdoor,
                PositionX = table.PositionX,
                PositionY = table.PositionY,
                Width = table.Width,
                Height = table.Height,
                Shape = table.Shape,
                Rotation = table.Rotation,
                Notes = table.Notes,
                QRCodeData = table.QRCodeData,
                QRCodeGeneratedAt = table.QRCodeGeneratedAt
            };

            _logger.LogInformation("Created table {TableNumber} with ID {TableId}", table.TableNumber, table.Id);
            return ApiResponse<TableDto>.SuccessWithData(tableDto, "Table created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating table");
            return ApiResponse<TableDto>.Failure("Failed to create table");
        }
    }
}
