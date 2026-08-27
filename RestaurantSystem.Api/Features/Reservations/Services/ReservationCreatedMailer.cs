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
    private readonly IEmailLanguageResolver _languages;
    private readonly IReservationQuickActionLinks _quickActionLinks;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<ReservationCreatedMailer> _logger;

    public ReservationCreatedMailer(
        IEmailService emailService,
        IEmailBrandingProvider brandingProvider,
        IEmailLanguageResolver languages,
        IReservationQuickActionLinks quickActionLinks,
        IOptions<EmailSettings> emailSettings,
        ILogger<ReservationCreatedMailer> logger)
    {
        ArgumentNullException.ThrowIfNull(emailSettings);

        _emailService = emailService;
        _brandingProvider = brandingProvider;
        _languages = languages;
        _quickActionLinks = quickActionLinks;
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

            // Two recipients, two languages, and they must not be the same value: the guest reads
            // the language frozen on the booking, the restaurant reads its own (§1 rank 4).
            var guestCulture = _languages.ForGuest(reservation.PreferredLanguage);
            var operatorCulture = _languages.ForOperator();

            var guest = new EmailGuest(
                reservation.CustomerName, reservation.CustomerEmail, reservation.CustomerPhone ?? string.Empty);
            // One description of the booking, but only the OPERATOR's copy carries buttons, so the
            // two signatures are minted here and simply ignored by the guest's template. Signed
            // over the status the booking is in right now, which is what makes a link stop working
            // the moment the booking is decided (backend #402).
            var details = new ReservationMailDetails(
                reservation.ReservationDate,
                reservation.StartTime,
                reservation.EndTime,
                reservation.NumberOfGuests,
                tableNumber,
                reservation.SpecialRequests,
                reservation.Id,
                _quickActionLinks.Mint(reservation.Id, ReservationQuickAction.Approve, reservation.Status),
                _quickActionLinks.Mint(reservation.Id, ReservationQuickAction.Reject, reservation.Status));

            // One reservation, two mails, ONE description of it — and two different cultures, which
            // is the thing that must not be shared (§6.10).
            await _emailService.SendReservationConfirmationEmailAsync(
                guestCulture, reservation.CustomerEmail, reservation.CustomerName, details);

            var brand = await _brandingProvider.GetAsync(cancellationToken);

            await _emailService.SendEmailAsync(
                _emailSettings.AdminEmail,
                EmailTemplates.ReservationAdminNotification.GetSubject(operatorCulture, brand),
                EmailTemplates.ReservationAdminNotification.GetHtmlBody(
                    operatorCulture,
                    brand,
                    guest,
                    details,
                    new EmailLinks(
                        _emailSettings.BackendBaseUrl, _emailSettings.FrontendBaseUrl, _emailSettings.AdminEmail)),
                EmailTemplates.ReservationAdminNotification.GetTextBody(
                    operatorCulture, brand, guest, details, _emailSettings.AdminEmail));

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
