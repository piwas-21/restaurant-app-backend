using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <inheritdoc cref="IReservationQuickActionPages"/>
public sealed class ReservationQuickActionPages : IReservationQuickActionPages
{
    private const string SuccessAccent = "#10b981";
    private const string ErrorAccent = "#dc2626";
    private const string NeutralAccent = "#374151";

    private readonly IHtmlResponseBuilder _html;
    private readonly EmailSettings _emailSettings;

    public ReservationQuickActionPages(IHtmlResponseBuilder html, IOptions<EmailSettings> emailSettings)
    {
        ArgumentNullException.ThrowIfNull(emailSettings);

        _html = html;
        _emailSettings = emailSettings.Value;
    }

    /// <inheritdoc />
    public string Approved(Guid reservationId) => Outcome(
        "Reservation Approved", "Reservation Approved!", "\u2713", SuccessAccent, reservationId,
        "approved", "The customer has been automatically notified via email.");

    /// <inheritdoc />
    public string Rejected(Guid reservationId) => Outcome(
        "Reservation Rejected", "Reservation Rejected", "\u2715", ErrorAccent, reservationId,
        "rejected", "The customer will be notified via email.");

    /// <inheritdoc />
    public string LinkNotUsable() => Build(new HtmlStatusPage
    {
        Title = "Link No Longer Usable",
        Icon = "\u23F1",
        AccentColor = NeutralAccent,
        Heading = "This link can no longer be used",
        Style = HtmlPageStyle.Card,
        ShowCloseLink = true,
        // Says nothing about whether the reservation exists, and nothing about WHICH of the
        // reasons applied. The real reason is in the server log.
        BodyHtml =
            "<p>Reservation links expire, and they stop working once the booking has been " +
            "approved or rejected \u2014 including by this same link.</p>" +
            "<p>Open the reservations dashboard to see the booking and decide it there.</p>" +
            $"<div class='details'><p><a href='{_html.Escape(_emailSettings.FrontendBaseUrl)}/admin/reservations'>" +
            "Go to the reservations dashboard</a></p></div>",
    });

    /// <inheritdoc />
    public string Failed(string message) => Build(new HtmlStatusPage
    {
        Title = "Error",
        Icon = "\u2715",
        AccentColor = ErrorAccent,
        Heading = "Error",
        Style = HtmlPageStyle.Card,
        ShowCloseLink = true,
        BodyHtml = $"<p>{_html.Escape(message)}</p>",
    });

    private string Outcome(
        string title, string heading, string icon, string accentColor,
        Guid reservationId, string outcomeVerb, string notice) => Build(new HtmlStatusPage
        {
            Title = title,
            Icon = icon,
            AccentColor = accentColor,
            Heading = heading,
            Style = HtmlPageStyle.Card,
            ShowCloseLink = true,
            BodyHtml =
                $"<p>The reservation has been successfully {_html.Escape(outcomeVerb)}.</p>" +
                "<div class='details'>" +
                $"<p><strong>Reservation ID:</strong> {_html.Escape(reservationId.ToString())}</p>" +
                $"<p>{_html.Escape(notice)}</p>" +
                "</div>",
        });

    private string Build(HtmlStatusPage page) => _html.BuildStatusPage(page);
}
