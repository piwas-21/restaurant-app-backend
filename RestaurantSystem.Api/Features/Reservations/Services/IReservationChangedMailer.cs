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
/// A contact-detail-only edit deliberately sends the restaurant NOTHING. It changes no decision
/// the restaurant has to take, the dashboard already shows the new number, and an alert that
/// arrives for a corrected phone number is how the alert that matters stops being read.
/// </para>
/// </remarks>
public interface IReservationChangedMailer
{
    Task SendAsync(
        Reservation reservation, string tableNumber, ReservationEdit edit, CancellationToken cancellationToken);
}
