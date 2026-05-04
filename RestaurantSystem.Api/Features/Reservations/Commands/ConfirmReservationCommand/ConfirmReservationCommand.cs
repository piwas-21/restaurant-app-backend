using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ConfirmReservationCommand;

public record ConfirmReservationCommand(Guid ReservationId) : ICommand<ApiResponse<bool>>;

public class ConfirmReservationCommandHandler : ICommandHandler<ConfirmReservationCommand, ApiResponse<bool>>
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<ConfirmReservationCommandHandler> _logger;

    public ConfirmReservationCommandHandler(
        ApplicationDbContext context,
        IEmailService emailService,
        ILogger<ConfirmReservationCommandHandler> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ApiResponse<bool>> Handle(ConfirmReservationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var reservation = await _context.Reservations
                .Include(r => r.Table)
                .FirstOrDefaultAsync(r => r.Id == command.ReservationId, cancellationToken);

            if (reservation == null)
            {
                return ApiResponse<bool>.Failure("Reservation not found");
            }

            if (reservation.Status == ReservationStatus.Confirmed)
            {
                return ApiResponse<bool>.Failure("Reservation is already confirmed");
            }

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                return ApiResponse<bool>.Failure("Cannot confirm a cancelled reservation");
            }

            if (reservation.Status == ReservationStatus.Completed)
            {
                return ApiResponse<bool>.Failure("Cannot confirm a completed reservation");
            }

            reservation.Status = ReservationStatus.Confirmed;
            await _context.SaveChangesAsync(cancellationToken);

            // Send confirmation email to customer
            try
            {
                await _emailService.SendReservationApprovedEmailAsync(
                    reservation.CustomerEmail,
                    reservation.CustomerName,
                    reservation.Table?.TableNumber ?? "N/A",
                    reservation.ReservationDate,
                    reservation.StartTime,
                    reservation.EndTime,
                    reservation.NumberOfGuests,
                    reservation.SpecialRequests,
                    reservation.Notes);

                _logger.LogInformation("Sent confirmation email for reservation {ReservationId} to {Email}",
                    reservation.Id, reservation.CustomerEmail);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send confirmation email for reservation {ReservationId}, but reservation was confirmed",
                    reservation.Id);
                // Don't fail the confirmation if email fails
            }

            _logger.LogInformation("Confirmed reservation {ReservationId}", reservation.Id);
            return ApiResponse<bool>.SuccessWithData(true, "Reservation confirmed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming reservation {ReservationId}", command.ReservationId);
            return ApiResponse<bool>.Failure("Failed to confirm reservation");
        }
    }
}
