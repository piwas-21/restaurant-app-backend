using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

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

            var table = new Table
            {
                TableNumber = data.TableNumber,
                MaxGuests = data.MaxGuests,
                IsActive = data.IsActive,
                IsOutdoor = data.IsOutdoor,
                PositionX = data.PositionX,
                PositionY = data.PositionY,
                Width = data.Width,
                Height = data.Height,
                Shape = data.Shape,
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
