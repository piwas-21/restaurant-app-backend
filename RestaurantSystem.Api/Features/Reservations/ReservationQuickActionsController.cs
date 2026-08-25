using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Reservations.Commands.CancelReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.ConfirmReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Queries.GetReservationForQuickActionQuery;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations;

/// <summary>
/// Email-link landing endpoints for reservations. Each handler returns a status page rendered via
/// <see cref="IReservationQuickActionPages"/>.
/// Extracted from <c>ReservationsController</c> in Sprint 2 task 2.14
/// (mirrors the <c>OrderQuickActionsController</c> split from task 2.5).
/// </summary>
/// <remarks>
/// Still <c>[AllowAnonymous]</c>, and that is not a gap: the links are opened from a mail client,
/// which carries no session. What authorises the caller is the <c>?token=</c> the alert mail put on
/// the link — an HMAC over the reservation, the action and the booking's CURRENT status
/// (<see cref="IReservationQuickActionLinks"/>, backend #402). Before that token existed, the bare
/// reservation id was the whole authorisation, and <c>POST /api/Reservations</c> hands that id to
/// whoever made the booking — so a guest could approve their own table.
/// </remarks>
[ApiController]
[RequireModule(ModuleIds.Reservations)]
[Route("api/reservations")]
[AllowAnonymous]
public class ReservationQuickActionsController : ControllerBase
{
    private readonly CustomMediator _mediator;
    private readonly IReservationQuickActionLinks _links;
    private readonly IReservationQuickActionPages _pages;
    private readonly ILogger<ReservationQuickActionsController> _logger;

    public ReservationQuickActionsController(
        CustomMediator mediator,
        IReservationQuickActionLinks links,
        IReservationQuickActionPages pages,
        ILogger<ReservationQuickActionsController> logger)
    {
        _mediator = mediator;
        _links = links;
        _pages = pages;
        _logger = logger;
    }

    /// <summary>Quick approve reservation from the restaurant's alert email.</summary>
    /// <param name="id">Reservation the link was minted for.</param>
    /// <param name="token">
    /// The link signature. Optional on the signature only so a token-less LEGACY link reaches the
    /// grace-window check instead of a 400 that would tell an attacker the parameter exists.
    /// </param>
    [HttpGet("{id}/quick-approve")]
    public async Task<IActionResult> QuickApprove(Guid id, [FromQuery] string? token = null)
    {
        if (!await AuthorizeAsync(id, ReservationQuickAction.Approve, token))
        {
            return Html(_pages.LinkNotUsable());
        }

        try
        {
            var result = await _mediator.SendCommand(new ConfirmReservationCommand(id));
            return result.Success
                ? Html(_pages.Approved(id))
                : Html(_pages.Failed(result.Message ?? "Failed to approve reservation"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving reservation {ReservationId}", id);
            return Html(_pages.Failed("An unexpected error occurred"));
        }
    }

    /// <summary>Quick reject reservation from the restaurant's alert email.</summary>
    /// <param name="id">Reservation the link was minted for.</param>
    /// <param name="token">See <see cref="QuickApprove"/>.</param>
    [HttpGet("{id}/quick-reject")]
    public async Task<IActionResult> QuickReject(Guid id, [FromQuery] string? token = null)
    {
        if (!await AuthorizeAsync(id, ReservationQuickAction.Reject, token))
        {
            return Html(_pages.LinkNotUsable());
        }

        try
        {
            // EnforceOwnership: false — the caller is the restaurant reading its own alert mail and
            // holds no session, so there is nobody to own anything. The link's signature is what
            // stands in for the missing credential.
            var result = await _mediator.SendCommand(new CancelReservationCommand(id, EnforceOwnership: false));
            if (result.Success)
            {
                _logger.LogInformation("Reservation {ReservationId} rejected via email action", id);
                return Html(_pages.Rejected(id));
            }

            return Html(_pages.Failed(result.Message ?? "Failed to reject reservation"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting reservation {ReservationId}", id);
            return Html(_pages.Failed("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// True only when the link proves it was issued for this reservation and this action. A
    /// missing reservation answers false, exactly like a bad token, so the caller renders one page
    /// for both and the route reveals nothing about which ids exist.
    /// </summary>
    private async Task<bool> AuthorizeAsync(Guid id, ReservationQuickAction action, string? token)
    {
        var reservation = await _mediator.SendQuery(new GetReservationForQuickActionQuery(id));
        if (reservation is null)
        {
            _logger.LogWarning("Refused quick-{Action} link: no such reservation {ReservationId}", action, id);
            return false;
        }

        return _links.Verify(id, action, reservation.Status, reservation.CreatedAt, token)
            != QuickActionLinkVerdict.Refused;
    }

    private ContentResult Html(string page) => Content(page, "text/html");
}
