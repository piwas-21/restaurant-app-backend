using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public partial class FidelityPointsService
{
    private async Task LockOrderRowForLoyaltyAsync(
        Guid orderId,
        Guid expectedUserId,
        CancellationToken cancellationToken)
    {
        var owners = await _context.Database.SqlQuery<Guid?>(
                $"SELECT user_id AS \"Value\" FROM orders WHERE id = {orderId} AND is_deleted = FALSE FOR UPDATE")
            .ToListAsync(cancellationToken);

        if (owners.Count != 1 || owners[0] != expectedUserId)
        {
            throw new ConflictException("The order is unavailable for this loyalty operation.");
        }
    }

    private async Task LockUserRowForLoyaltyAsync(Guid userId, CancellationToken cancellationToken)
    {
        var lockedUsers = await _context.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM \"Users\" WHERE id = {userId} AND is_deleted = FALSE FOR NO KEY UPDATE")
            .ToListAsync(cancellationToken);

        if (lockedUsers.Count != 1 || lockedUsers[0] != userId)
        {
            throw new ConflictException("The loyalty account owner is unavailable.");
        }
    }

    private async Task<FidelityPointBalance?> LockBalanceRowForLoyaltyAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var persisted = await _context.FidelityPointBalances
            .FromSqlInterpolated(
                $"SELECT * FROM fidelity_point_balances WHERE user_id = {userId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (persisted.Count > 1)
        {
            throw new ConflictException("The loyalty balance history requires reconciliation.");
        }

        var trackedBalances = _context.FidelityPointBalances.Local
            .Where(value => value.UserId == userId)
            .ToList();
        if (trackedBalances.Count > 1)
        {
            throw new ConflictException("The tracked loyalty balance history requires reconciliation.");
        }

        var tracked = trackedBalances.SingleOrDefault();
        if (persisted.Count == 0)
        {
            if (tracked is null)
            {
                return null;
            }

            if (_context.Entry(tracked).State == EntityState.Added)
            {
                return tracked;
            }

            throw new ConflictException("The tracked loyalty balance no longer exists.");
        }

        var fresh = persisted[0];
        if (tracked is null)
        {
            _context.FidelityPointBalances.Attach(fresh);
            return fresh;
        }

        var entry = _context.Entry(tracked);
        if (tracked.Id != fresh.Id || entry.State != EntityState.Unchanged)
        {
            throw new ConflictException("The tracked loyalty balance changed before it was locked.");
        }

        // A tracking raw-SQL query can reuse stale local values. Copy the locked, no-tracking row
        // into the existing entity before applying this operation's delta.
        entry.CurrentValues.SetValues(fresh);
        entry.OriginalValues.SetValues(fresh);
        entry.State = EntityState.Unchanged;
        return tracked;
    }
}
