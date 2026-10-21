using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>Read the final database-generated revision after all parent and child saves, before commit/mail.</summary>
public static class ReservationMailRevision
{
    public static Task RefreshTrackedAsync(ApplicationDbContext context, Reservation reservation, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A saved mail snapshot requires its owning booking transaction.");
        return context.Entry(reservation).ReloadAsync(cancellationToken);
    }
}
