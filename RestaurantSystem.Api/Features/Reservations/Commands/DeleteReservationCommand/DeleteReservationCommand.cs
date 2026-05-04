using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.DeleteReservationCommand;

public record DeleteReservationCommand(Guid ReservationId) : ICommand<ApiResponse<bool>>;

public class DeleteReservationCommandHandler : ICommandHandler<DeleteReservationCommand, ApiResponse<bool>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteReservationCommandHandler> _logger;

    public DeleteReservationCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<DeleteReservationCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<bool>> Handle(DeleteReservationCommand command, CancellationToken cancellationToken)
    {
        // Check if reservation exists
        var reservation = await _context.Reservations
            .FirstOrDefaultAsync(r => r.Id == command.ReservationId, cancellationToken);

        if (reservation == null)
        {
            return ApiResponse<bool>.Failure("Reservation not found");
        }

        // Hard delete the reservation - this will automatically free the table
        // since the reservation entry links the table to a specific time slot
        var rowsDeleted = await _context.Reservations
            .Where(r => r.Id == command.ReservationId)
            .ExecuteDeleteAsync(cancellationToken);

        if (rowsDeleted == 0)
        {
            return ApiResponse<bool>.Failure("Failed to delete reservation");
        }

        _logger.LogInformation(
            "Reservation with ID {ReservationId} permanently deleted by user {UserId}",
            command.ReservationId,
            _currentUserService.UserId);

        return ApiResponse<bool>.SuccessWithData(true, "Reservation permanently deleted");
    }
}
