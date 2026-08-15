using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>
/// The two mails a new reservation sends: the guest's "we have it" (M11) and the restaurant's
/// alert with the approve/reject action links (M15).
/// </summary>
/// <remarks>
/// Extracted from <c>CreateReservationCommandHandler</c> in GAP-2 S4 — that file was at its
/// 200-LOC command/handler limit (CLAUDE.md §4), and S5 has to touch both of these send calls
/// again to give them a culture. Neither mail may fail the reservation: it exists the moment it is
/// saved, and a mail provider having a bad minute is not a reason to tell a guest their table did
/// not happen.
/// </remarks>
public interface IReservationCreatedMailer
{
    Task SendAsync(Reservation reservation, string tableNumber, CancellationToken cancellationToken);
}
