namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>
/// The four pages the anonymous reservation email links can land on, as ready-to-serve HTML.
/// </summary>
/// <remarks>
/// Split out of <c>ReservationQuickActionsController</c> rather than left inline: the controller
/// has a hard 150-LOC ceiling (CLAUDE.md §4) and page copy is not dispatch logic. Returning a
/// string rather than an <c>IActionResult</c> keeps MVC types out of the service layer.
/// </remarks>
public interface IReservationQuickActionPages
{
    /// <summary>Booking confirmed. The guest has been mailed by the confirm handler.</summary>
    string Approved(Guid reservationId);

    /// <summary>Booking turned down.</summary>
    string Rejected(Guid reservationId);

    /// <summary>
    /// The single page for EVERY refusal: no token, wrong token, expired token, a booking that was
    /// already decided, and an id that does not exist. One page for all five on purpose — a
    /// distinct "no such reservation" would let anyone probe which ids are real.
    /// </summary>
    string LinkNotUsable();

    /// <summary>The action was authorised but the command declined it.</summary>
    string Failed(string message);
}
