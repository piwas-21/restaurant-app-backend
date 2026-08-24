using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CancelReservationCommand;

/// <param name="ReservationId">Reservation to cancel.</param>
/// <param name="EnforceOwnership">
/// When true (the default, and the only value a user-facing route may use), a non-staff caller may
/// cancel only a reservation they own; everyone else gets the not-found response. Set to false ONLY
/// for the [AllowAnonymous] quick-reject link the restaurant's own alert mail carries, which has no
/// caller at all. Defaulting to true keeps a new route secure unless it explicitly opts out.
/// [BindNever] on both the parameter and the property so a future [FromQuery]/[FromBody] binding
/// cannot reopen this from the wire (same guard as GetOrderByIdQuery).
/// </param>
public record CancelReservationCommand(
    Guid ReservationId,
    [BindNever][property: BindNever] bool EnforceOwnership = true) : ICommand<ApiResponse<bool>>;

public class CancelReservationCommandHandler : ICommandHandler<CancelReservationCommand, ApiResponse<bool>>
{
    private const string NotFoundMessage = "Reservation not found";

    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly IEmailBrandingProvider _brandingProvider;
    private readonly IEmailLanguageResolver _languages;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<CancelReservationCommandHandler> _logger;

    public CancelReservationCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IEmailService emailService,
        IEmailBrandingProvider brandingProvider,
        IEmailLanguageResolver languages,
        IOptions<EmailSettings> emailSettings,
        ILogger<CancelReservationCommandHandler> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _emailService = emailService;
        _brandingProvider = brandingProvider;
        _languages = languages;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    // Staff cancel any booking; a customer cancels only their own. Guest reservations
    // (CustomerId == null) are deliberately unreachable: matching a null owner against a null
    // caller would hand every guest booking to any signed-in user.
    private bool CanCurrentUserCancel(Reservation reservation) =>
        _currentUser.IsStaff
        || (_currentUser.UserId.HasValue && reservation.CustomerId == _currentUser.UserId.Value);

    public async Task<ApiResponse<bool>> Handle(CancelReservationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(r => r.Id == command.ReservationId, cancellationToken);

            if (reservation == null)
            {
                return ApiResponse<bool>.Failure(NotFoundMessage);
            }

            if (command.EnforceOwnership && !CanCurrentUserCancel(reservation))
            {
                // Word for word the missing-reservation answer, on purpose: a distinct 403 would
                // confirm the id exists and let anyone enumerate other guests' bookings. The real
                // reason is recorded server-side only.
                _logger.LogWarning(
                    "User {UserId} denied cancellation of reservation {ReservationId} they do not own; responding as not-found",
                    _currentUser.UserId, command.ReservationId);
                return ApiResponse<bool>.Failure(NotFoundMessage);
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
                var brand = await _brandingProvider.GetAsync(cancellationToken);

                // A cancellation is always a staff action, so the language is the booking's own.
                var culture = _languages.ForGuest(reservation.PreferredLanguage);

                await _emailService.SendEmailAsync(
                    reservation.CustomerEmail,
                    Common.Templates.EmailTemplates.ReservationRejected.GetSubject(culture, brand),
                    Common.Templates.EmailTemplates.ReservationRejected.GetHtmlBody(culture,
                        brand,
                        reservation.CustomerName,
                        reservation.ReservationDate,
                        reservation.StartTime,
                        reservation.NumberOfGuests,
                        _emailSettings.AdminEmail
                    ),
                    Common.Templates.EmailTemplates.ReservationRejected.GetTextBody(culture,
                        brand,
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
