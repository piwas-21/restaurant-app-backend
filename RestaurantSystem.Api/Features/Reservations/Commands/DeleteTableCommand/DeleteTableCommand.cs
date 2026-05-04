using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.DeleteTableCommand;

public record DeleteTableCommand(Guid TableId) : ICommand<ApiResponse<bool>>;

public class DeleteTableCommandHandler : ICommandHandler<DeleteTableCommand, ApiResponse<bool>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DeleteTableCommandHandler> _logger;

    public DeleteTableCommandHandler(ApplicationDbContext context, ILogger<DeleteTableCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<bool>> Handle(DeleteTableCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var table = await _context.Tables
                .Include(t => t.Reservations)
                .FirstOrDefaultAsync(t => t.Id == command.TableId, cancellationToken);

            if (table == null)
            {
                return ApiResponse<bool>.Failure("Table not found");
            }

            // Check if table has any reservations
            if (table.Reservations.Any())
            {
                return ApiResponse<bool>.Failure("Cannot delete table with existing reservations. Please cancel or reassign reservations first.");
            }

            _context.Tables.Remove(table);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted table {TableId}", command.TableId);
            return ApiResponse<bool>.SuccessWithData(true, "Table deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting table {TableId}", command.TableId);
            return ApiResponse<bool>.Failure("Failed to delete table");
        }
    }
}
