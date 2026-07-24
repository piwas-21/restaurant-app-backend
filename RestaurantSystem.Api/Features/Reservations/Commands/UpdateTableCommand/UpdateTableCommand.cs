using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.UpdateTableCommand;

public record UpdateTableCommand(Guid TableId, UpdateTableDto TableData) : ICommand<ApiResponse<TableDto>>;

public class UpdateTableCommandHandler : ICommandHandler<UpdateTableCommand, ApiResponse<TableDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UpdateTableCommandHandler> _logger;

    public UpdateTableCommandHandler(ApplicationDbContext context, ILogger<UpdateTableCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<TableDto>> Handle(UpdateTableCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var table = await _context.Tables.FindAsync(new object[] { command.TableId }, cancellationToken);

            if (table == null)
            {
                return ApiResponse<TableDto>.Failure("Table not found");
            }

            // Check if table number is being changed to an existing one
            if (table.TableNumber != command.TableData.TableNumber)
            {
                var existingTable = await _context.Tables
                    .FirstOrDefaultAsync(t => t.TableNumber == command.TableData.TableNumber, cancellationToken);

                if (existingTable != null)
                {
                    return ApiResponse<TableDto>.Failure($"Table with number '{command.TableData.TableNumber}' already exists");
                }
            }

            table.TableNumber = command.TableData.TableNumber;
            table.MaxGuests = command.TableData.MaxGuests;
            table.IsActive = command.TableData.IsActive;
            table.IsOutdoor = command.TableData.IsOutdoor;
            table.PositionX = command.TableData.PositionX;
            table.PositionY = command.TableData.PositionY;

            // Geometry is patch-style: only overwrite what the caller supplied.
            // The admin table-layout editor stopped sending these four fields
            // (frontend PR #272), and assigning them unconditionally wrote the
            // DTO's defaults over the stored values — shape → "circle" and
            // rotation → 0 landed silently, while width/height → 0 failed
            // [Range(10, 500)] and rejected the entire save with a 400.
            // FLOOR-PLAN-REVAMP slice S1 migrates the surviving values to
            // metres, so they must not be clobbered here.
            if (command.TableData.Width.HasValue)
            {
                table.Width = command.TableData.Width.Value;
            }

            if (command.TableData.Height.HasValue)
            {
                table.Height = command.TableData.Height.Value;
            }

            // Blank is treated as "not supplied" too: the shape column was
            // created NOT NULL DEFAULT '', so an empty string is a legacy
            // placeholder to preserve, never a shape worth writing.
            if (!string.IsNullOrWhiteSpace(command.TableData.Shape))
            {
                table.Shape = command.TableData.Shape;
            }

            if (command.TableData.Rotation.HasValue)
            {
                table.Rotation = command.TableData.Rotation.Value;
            }

            table.Notes = command.TableData.Notes;

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

            _logger.LogInformation("Updated table {TableId}", command.TableId);
            return ApiResponse<TableDto>.SuccessWithData(tableDto, "Table updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating table {TableId}", command.TableId);
            return ApiResponse<TableDto>.Failure("Failed to update table");
        }
    }
}
