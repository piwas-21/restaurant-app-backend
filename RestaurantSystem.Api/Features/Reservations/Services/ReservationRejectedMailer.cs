using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <inheritdoc cref="IReservationRejectedMailer"/>
public sealed class ReservationRejectedMailer(
    IEmailService emailService,
    IEmailBrandingProvider brandingProvider,
    IEmailLanguageResolver languages,
    IOptions<EmailSettings> emailSettings,
    ILogger<ReservationRejectedMailer> logger) : IReservationRejectedMailer
{
    public async Task SendAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        try
        {
            var brand = await brandingProvider.GetAsync(cancellationToken);
            // The booking's saved language applies to customer and staff cancellations alike.
            var culture = languages.ForGuest(reservation.PreferredLanguage);
            await emailService.SendEmailAsync(
                reservation.CustomerEmail,
                EmailTemplates.ReservationRejected.GetSubject(culture, brand),
                EmailTemplates.ReservationRejected.GetHtmlBody(culture, brand,
                    reservation.CustomerName, reservation.ReservationDate, reservation.StartTime,
                    reservation.NumberOfGuests, emailSettings.Value.AdminEmail),
                EmailTemplates.ReservationRejected.GetTextBody(culture, brand,
                    reservation.CustomerName, reservation.ReservationDate, reservation.StartTime,
                    reservation.NumberOfGuests, emailSettings.Value.AdminEmail));
            logger.LogInformation("Rejection email sent for reservation {ReservationId}", reservation.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send rejection email for reservation {ReservationId}, but reservation was cancelled",
                reservation.Id);
        }
    }
}
