using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <inheritdoc cref="IReservationCreatedMailer"/>
public class ReservationCreatedMailer : IReservationCreatedMailer
{
    private readonly IEmailService _emailService;
    private readonly IEmailBrandingProvider _brandingProvider;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<ReservationCreatedMailer> _logger;

    public ReservationCreatedMailer(
        IEmailService emailService,
        IEmailBrandingProvider brandingProvider,
        IOptions<EmailSettings> emailSettings,
        ILogger<ReservationCreatedMailer> logger)
    {
        ArgumentNullException.ThrowIfNull(emailSettings);

        _emailService = emailService;
        _brandingProvider = brandingProvider;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task SendAsync(Reservation reservation, string tableNumber, CancellationToken cancellationToken)
    {
        // Inside the try with everything else: the reservation is saved before this is called, so
        // NOTHING in here — including an argument this method disagrees with — may turn into a
        // "Failed to create reservation" for a guest whose table is booked.
        // Swallowed on purpose, exactly as before the extraction: the reservation is already saved,
        // and failing the request over a mail would tell a guest their table did not happen.
        try
        {
            ArgumentNullException.ThrowIfNull(reservation);

            await _emailService.SendReservationConfirmationEmailAsync(EmailCultures.English,
                reservation.CustomerEmail,
                reservation.CustomerName,
                tableNumber,
                reservation.ReservationDate,
                reservation.StartTime,
                reservation.EndTime,
                reservation.NumberOfGuests,
                reservation.SpecialRequests);

            var brand = await _brandingProvider.GetAsync(cancellationToken);

            await _emailService.SendEmailAsync(
                _emailSettings.AdminEmail,
                EmailTemplates.ReservationAdminNotification.GetSubject(EmailCultures.English, brand),
                EmailTemplates.ReservationAdminNotification.GetHtmlBody(EmailCultures.English,
                    brand,
                    reservation.Id,
                    reservation.CustomerName,
                    reservation.CustomerEmail,
                    reservation.CustomerPhone ?? string.Empty,
                    reservation.ReservationDate,
                    reservation.StartTime,
                    reservation.EndTime,
                    reservation.NumberOfGuests,
                    tableNumber,
                    _emailSettings.BackendBaseUrl,
                    _emailSettings.FrontendBaseUrl,
                    _emailSettings.AdminEmail,
                    reservation.SpecialRequests),
                EmailTemplates.ReservationAdminNotification.GetTextBody(EmailCultures.English,
                    brand,
                    reservation.Id,
                    reservation.CustomerName,
                    reservation.CustomerEmail,
                    reservation.CustomerPhone ?? string.Empty,
                    reservation.ReservationDate,
                    reservation.StartTime,
                    reservation.EndTime,
                    reservation.NumberOfGuests,
                    tableNumber,
                    _emailSettings.AdminEmail,
                    reservation.SpecialRequests));

            _logger.LogInformation("Confirmation emails sent for reservation {ReservationId}", reservation.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send confirmation emails for reservation {ReservationId}, but reservation was created",
                reservation.Id);
        }
    }
}
