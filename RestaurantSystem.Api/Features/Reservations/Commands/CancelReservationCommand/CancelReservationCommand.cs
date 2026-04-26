using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CancelReservationCommand;

public record CancelReservationCommand(Guid ReservationId) : ICommand<ApiResponse<bool>>;

public class CancelReservationCommandHandler : ICommandHandler<CancelReservationCommand, ApiResponse<bool>>
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<CancelReservationCommandHandler> _logger;

    public CancelReservationCommandHandler(
        ApplicationDbContext context,
        IEmailService emailService,
        IOptions<EmailSettings> emailSettings,
        ILogger<CancelReservationCommandHandler> logger)
    {
        _context = context;
        _emailService = emailService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ApiResponse<bool>> Handle(CancelReservationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(r => r.Id == command.ReservationId, cancellationToken);

            if (reservation == null)
            {
                return ApiResponse<bool>.Failure("Reservation not found");
            }

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                return ApiResponse<bool>.Failure("Reservation is already cancelled");
            }

            if (reservation.Status == ReservationStatus.Completed)
            {
                return ApiResponse<bool>.Failure("Cannot cancel a completed reservation");
            }

            reservation.Status = ReservationStatus.Cancelled;
            await _context.SaveChangesAsync(cancellationToken);

            // Send rejection email to customer
            try
            {
                await _emailService.SendEmailAsync(
                    reservation.CustomerEmail,
                    Common.Templates.EmailTemplates.ReservationRejected.Subject,
                    Common.Templates.EmailTemplates.ReservationRejected.GetHtmlBody(
                        reservation.CustomerName,
                        reservation.ReservationDate,
                        reservation.StartTime,
                        reservation.NumberOfGuests,
                        _emailSettings.AdminEmail
                    ),
                    Common.Templates.EmailTemplates.ReservationRejected.GetTextBody(
                        reservation.CustomerName,
                        reservation.ReservationDate,
                        reservation.StartTime,
                        reservation.NumberOfGuests,
                        _emailSettings.AdminEmail
                    ));

                _logger.LogInformation("Rejection email sent for reservation {ReservationId}", reservation.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send rejection email for reservation {ReservationId}, but reservation was cancelled", reservation.Id);
                // Don't fail the cancellation if email fails
            }

            _logger.LogInformation("Cancelled reservation {ReservationId}", reservation.Id);
            return ApiResponse<bool>.SuccessWithData(true, "Reservation cancelled successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling reservation {ReservationId}", command.ReservationId);
            return ApiResponse<bool>.Failure("Failed to cancel reservation");
        }
    }
}
