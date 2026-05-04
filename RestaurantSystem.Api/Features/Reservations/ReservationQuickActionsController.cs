using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Commands.CancelReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.ConfirmReservationCommand;

namespace RestaurantSystem.Api.Features.Reservations;

/// <summary>
/// Email-link landing endpoints for reservations. Each handler returns a
/// status page rendered via <see cref="IHtmlResponseBuilder"/> (task 2.1) —
/// all values flowing into HTML are routed through <c>_html.Escape</c>; the
/// hardcoded accent colours satisfy the regex-validated CSS-color contract
/// on <see cref="HtmlStatusPage.AccentColor"/>.
/// Extracted from <c>ReservationsController</c> in Sprint 2 task 2.14
/// (mirrors the <c>OrderQuickActionsController</c> split from task 2.5).
/// </summary>
[ApiController]
[Route("api/reservations")]
[AllowAnonymous]
public class ReservationQuickActionsController : ControllerBase
{
    private const string SuccessAccent = "#10b981";
    private const string ErrorAccent = "#dc2626";

    private readonly CustomMediator _mediator;
    private readonly IHtmlResponseBuilder _html;
    private readonly ILogger<ReservationQuickActionsController> _logger;

    public ReservationQuickActionsController(
        CustomMediator mediator,
        IHtmlResponseBuilder html,
        ILogger<ReservationQuickActionsController> logger)
    {
        _mediator = mediator;
        _html = html;
        _logger = logger;
    }

    /// <summary>Quick approve reservation from email link.</summary>
    [HttpGet("{id}/quick-approve")]
    public async Task<IActionResult> QuickApprove(Guid id)
    {
        try
        {
            var result = await _mediator.SendCommand(new ConfirmReservationCommand(id));
            return result.Success
                ? OutcomePage("Reservation Approved", "Reservation Approved!", "✓", SuccessAccent, id, "approved", customerNotified: true)
                : ErrorPage(result.Message ?? "Failed to approve reservation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving reservation {ReservationId}", id);
            return ErrorPage("An unexpected error occurred");
        }
    }

    /// <summary>Quick reject reservation from email link.</summary>
    [HttpGet("{id}/quick-reject")]
    public async Task<IActionResult> QuickReject(Guid id)
    {
        try
        {
            var result = await _mediator.SendCommand(new CancelReservationCommand(id));
            if (result.Success)
            {
                // TODO: send rejection email via EmailTemplates.ReservationRejected.
                _logger.LogInformation("Reservation {ReservationId} rejected via email action", id);
                return OutcomePage("Reservation Rejected", "Reservation Rejected", "✕", ErrorAccent, id, "rejected", customerNotified: false);
            }
            return ErrorPage(result.Message ?? "Failed to reject reservation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting reservation {ReservationId}", id);
            return ErrorPage("An unexpected error occurred");
        }
    }

    private ContentResult OutcomePage(
        string title, string heading, string icon, string accentColor,
        Guid reservationId, string outcomeVerb, bool customerNotified)
    {
        var notice = customerNotified
            ? "The customer has been automatically notified via email."
            : "The customer will be notified via email.";

        return Html(new HtmlStatusPage
        {
            Title = title,
            Icon = icon,
            AccentColor = accentColor,
            Heading = heading,
            Style = HtmlPageStyle.Card,
            ShowCloseLink = true,
            BodyHtml =
                $"<p>The reservation has been successfully {_html.Escape(outcomeVerb)}.</p>" +
                $"<div class='details'>" +
                $"<p><strong>Reservation ID:</strong> {_html.Escape(reservationId.ToString())}</p>" +
                $"<p>{_html.Escape(notice)}</p>" +
                $"</div>",
        });
    }

    private ContentResult ErrorPage(string message) => Html(new HtmlStatusPage
    {
        Title = "Error",
        Icon = "✕",
        AccentColor = ErrorAccent,
        Heading = "Error",
        Style = HtmlPageStyle.Card,
        ShowCloseLink = true,
        BodyHtml = $"<p>{_html.Escape(message)}</p>",
    });

    private ContentResult Html(HtmlStatusPage page) =>
        Content(_html.BuildStatusPage(page), "text/html");
}
