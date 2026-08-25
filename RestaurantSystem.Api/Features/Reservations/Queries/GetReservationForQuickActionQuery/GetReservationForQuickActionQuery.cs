using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetReservationForQuickActionQuery;

/// <summary>The only three fields the anonymous email-link actions need to decide what to render.</summary>
/// <remarks>
/// Deliberately not <c>ReservationDto</c>, for the same reason <c>QuickActionOrder</c> is not
/// <c>OrderDto</c>: this is a reservation lookup reachable with no credentials at all, and the DTO
/// carries the guest's name, email, phone and notes. Projecting to three non-personal fields means
/// a later addition to <c>ReservationDto</c> cannot silently widen this surface.
/// </remarks>
/// <param name="Status">What the token has to have been signed over to be valid.</param>
/// <param name="CreatedAt">Anchor of the legacy grace window (backend #402).</param>
public sealed record QuickActionReservation(Guid Id, ReservationStatus Status, DateTime CreatedAt);

/// <summary>
/// Resolves the reservation behind a quick-approve / quick-reject email link. Returns null when
/// there is no such reservation; the caller must render the SAME page for null as for a bad token,
/// so the route cannot be used to test which ids exist.
/// </summary>
public record GetReservationForQuickActionQuery(Guid ReservationId) : IQuery<QuickActionReservation?>;

public class GetReservationForQuickActionQueryHandler
    : IQueryHandler<GetReservationForQuickActionQuery, QuickActionReservation?>
{
    private readonly ApplicationDbContext _context;

    public GetReservationForQuickActionQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<QuickActionReservation?> Handle(
        GetReservationForQuickActionQuery query,
        CancellationToken cancellationToken) =>
        _context.Reservations
            .AsNoTracking()
            .Where(r => r.Id == query.ReservationId)
            .Select(r => new QuickActionReservation(r.Id, r.Status, r.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
}
