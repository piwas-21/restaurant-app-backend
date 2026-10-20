using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>
/// The mails a guest's own edit of their booking sends: the guest's written record (M16) and,
/// when the booked SHAPE moved, the restaurant's "this needs a new decision" alert (M17) with the
/// approve / reject links.
/// </summary>
/// <remarks>
/// Backend #407. Neither mail may fail the update: the booking is saved before this runs, and a
/// mail provider having a bad minute is not a reason to tell a guest their change was lost — the
/// same policy every other reservation mail already follows.
/// <para>
/// A Confirmed contact-only correction sends no operator alert. A Pending correction refreshes
/// the operator's buttons because every saved revision retires its previous email authority.
/// </para>
/// </remarks>
public interface IReservationChangedMailer
{
    Task SendAsync(
        Reservation reservation, string tableNumber, ReservationEdit edit, CancellationToken cancellationToken);
}
