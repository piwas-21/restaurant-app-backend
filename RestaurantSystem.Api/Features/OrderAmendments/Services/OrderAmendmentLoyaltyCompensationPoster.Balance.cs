using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentLoyaltyCompensationPoster
{
    private static async Task<int> ComputeSafeBalanceAfterClawbackAsync(
        ApplicationDbContext context, FidelityPointBalance balance, Guid userId, long totalClawback,
        CancellationToken cancellationToken)
    {
        if (balance.CurrentPoints < 0)
            throw new ConflictException("The loyalty balance is negative and requires reconciliation.");
        var active = await OrderAmendmentLoyaltyReservationEvidence.ReadOutstandingAsync(
            context, userId, cancellationToken);
        if (active < totalClawback || totalClawback > balance.CurrentPoints
            || totalClawback > 0 && balance.CurrentPoints < active)
            throw new ConflictException("The exact loyalty point obligation is no longer fully reserved.");
        var afterClawback = checked(balance.CurrentPoints - checked((int)totalClawback));
        if (totalClawback > 0 && afterClawback < active - totalClawback)
            throw new ConflictException("The clawback would consume points reserved for another exact obligation.");
        return afterClawback;
    }

    private static async Task ConsumeReservationsAsync(
        ApplicationDbContext context, OrderAmendmentResolutionOperation operation,
        long totalClawback, DateTime now, string audit, CancellationToken cancellationToken)
    {
        var reservations = await context.OrderAmendmentLoyaltyReservations
            .Where(value => value.OperationId == operation.Id).ToArrayAsync(cancellationToken);
        if (reservations.Length != (totalClawback > 0 ? 1 : 0))
            throw Invalid();
        foreach (var reservation in reservations)
        {
            if (reservation.State != OrderAmendmentLoyaltyReservationState.Reserved
                || reservation.SourceOrderId != operation.SourceOrderId)
                throw Invalid();
            reservation.State = OrderAmendmentLoyaltyReservationState.Consumed;
            reservation.UpdatedAt = now;
            reservation.UpdatedBy = audit;
        }
    }

    private static async Task<FidelityPointBalance> LockTrackedBalanceAsync(
        ApplicationDbContext context, Guid userId, CancellationToken cancellationToken)
    {
        var persisted = await context.FidelityPointBalances
            .FromSqlInterpolated($"SELECT * FROM fidelity_point_balances WHERE user_id = {userId} FOR UPDATE")
            .AsNoTracking().Take(2).ToArrayAsync(cancellationToken);
        if (persisted.Length != 1)
            throw new ConflictException("The original loyalty balance is unavailable for exact compensation.");
        var tracked = context.FidelityPointBalances.Local.SingleOrDefault(value => value.UserId == userId);
        if (tracked is null)
        {
            context.FidelityPointBalances.Attach(persisted[0]);
            return persisted[0];
        }
        var entry = context.Entry(tracked);
        if (entry.State != EntityState.Unchanged || tracked.Id != persisted[0].Id)
            throw new ConflictException("The tracked loyalty balance changed before exact compensation.");
        entry.CurrentValues.SetValues(persisted[0]);
        entry.OriginalValues.SetValues(persisted[0]);
        entry.State = EntityState.Unchanged;
        return tracked;
    }
}
