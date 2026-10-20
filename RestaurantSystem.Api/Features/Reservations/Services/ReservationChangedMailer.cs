using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <inheritdoc cref="IReservationChangedMailer"/>
public class ReservationChangedMailer : IReservationChangedMailer
{
    private readonly IEmailService _emailService;
    private readonly IEmailBrandingProvider _brandingProvider;
    private readonly IEmailLanguageResolver _languages;
    private readonly IReservationQuickActionLinks _quickActionLinks;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<ReservationChangedMailer> _logger;

    public ReservationChangedMailer(
        IEmailService emailService,
        IEmailBrandingProvider brandingProvider,
        IEmailLanguageResolver languages,
        IReservationQuickActionLinks quickActionLinks,
        IOptions<EmailSettings> emailSettings,
        ILogger<ReservationChangedMailer> logger)
    {
        ArgumentNullException.ThrowIfNull(emailSettings);

        _emailService = emailService;
        _brandingProvider = brandingProvider;
        _languages = languages;
        _quickActionLinks = quickActionLinks;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        Reservation reservation, string tableNumber, ReservationEdit edit, CancellationToken cancellationToken)
    {
        // The one thing OUTSIDE the try. Everything a mail provider can do at run time is caught
        // below, because the update is already committed and no failure of it may reach the guest;
        // a null reservation is not that — the parameter is non-nullable, so it is a programmer
        // error at the single call site, and swallowing it would only hide the mails going missing.
        ArgumentNullException.ThrowIfNull(reservation);

        try
        {
            // Use the writer's final saved revision, read after every parent/child effect.
            // A return to Pending never makes an earlier email authorize a different proposal.
            var details = new ReservationMailDetails(
                reservation.ReservationDate,
                reservation.StartTime,
                reservation.EndTime,
                reservation.NumberOfGuests,
                tableNumber,
                reservation.SpecialRequests,
                reservation.Id,
                _quickActionLinks.Mint(reservation.Id, ReservationQuickAction.Approve, reservation.Status, reservation.Revision),
                _quickActionLinks.Mint(reservation.Id, ReservationQuickAction.Reject, reservation.Status, reservation.Revision));

            // The guest reads the language frozen on the booking, the restaurant its own (§1 rank
            // 4) — two values that must not become one.
            await _emailService.SendReservationChangedEmailAsync(
                _languages.ForGuest(reservation.PreferredLanguage),
                reservation.CustomerEmail,
                reservation.CustomerName,
                details,
                OutcomeOf(reservation, edit));

            // Every Pending edit retires the previous revision's buttons, including contact-only/no-op corrections.
            if (edit.ShapeChanged || reservation.Status == ReservationStatus.Pending)
            {
                await SendOperatorAlertAsync(reservation, details, edit, cancellationToken);
            }

            _logger.LogInformation(
                "Reservation changed emails sent for reservation {ReservationId} (shape changed: {ShapeChanged})",
                reservation.Id, edit.ShapeChanged);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send the changed-reservation emails for reservation {ReservationId}, but the "
                + "reservation was updated",
                reservation.Id);
        }
    }

    /// <summary>
    /// Which of the three sentences the guest's mail ends on. Read from the SAVED status plus the
    /// state the edit found: the row itself can no longer tell an approval that was withdrawn from
    /// a booking that was never approved.
    /// </summary>
    private static ReservationChangeOutcome OutcomeOf(Reservation reservation, ReservationEdit edit)
    {
        if (reservation.Status == ReservationStatus.Confirmed)
        {
            return ReservationChangeOutcome.StillConfirmed;
        }

        return edit.WasConfirmed
            ? ReservationChangeOutcome.NeedsApprovalAgain
            : ReservationChangeOutcome.AwaitingApproval;
    }

    private async Task SendOperatorAlertAsync(
        Reservation reservation,
        ReservationMailDetails details,
        ReservationEdit edit,
        CancellationToken cancellationToken)
    {
        var operatorCulture = _languages.ForOperator();
        var brand = await _brandingProvider.GetAsync(cancellationToken);
        var guest = new EmailGuest(
            reservation.CustomerName, reservation.CustomerEmail, reservation.CustomerPhone ?? string.Empty);
        var previous = new ReservationPreviousBooking(
            edit.PreviousDate, edit.PreviousStartTime, edit.PreviousEndTime, edit.PreviousGuests, edit.WasConfirmed);

        await _emailService.SendEmailAsync(
            _emailSettings.AdminEmail,
            EmailTemplates.ReservationChangedAdmin.GetSubject(operatorCulture, brand),
            EmailTemplates.ReservationChangedAdmin.GetHtmlBody(
                operatorCulture,
                brand,
                guest,
                details,
                previous,
                new EmailLinks(
                    _emailSettings.BackendBaseUrl, _emailSettings.FrontendBaseUrl, _emailSettings.AdminEmail)),
            EmailTemplates.ReservationChangedAdmin.GetTextBody(
                operatorCulture, brand, guest, details, previous, _emailSettings.AdminEmail));
    }
}
