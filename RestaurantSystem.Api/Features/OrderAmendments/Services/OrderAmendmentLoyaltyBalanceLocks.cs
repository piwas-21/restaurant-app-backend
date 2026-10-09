using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentLoyaltyBalanceLocks
{
    internal static async Task<bool> LockUserAsync(ApplicationDbContext context,
        Guid userId, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM "Users"
            WHERE id = {userId} AND is_deleted = FALSE FOR NO KEY UPDATE
            """).Take(2).ToListAsync(cancellationToken);
        return rows.Count == 1 && rows[0] == userId;
    }

    internal static async Task<int?> LockBalanceValueAsync(ApplicationDbContext context,
        Guid userId, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQuery<int>($"""
            SELECT current_points AS "Value" FROM fidelity_point_balances
            WHERE user_id = {userId} FOR UPDATE
            """).Take(2).ToListAsync(cancellationToken);
        if (rows.Count > 1)
            throw new ConflictException("The loyalty balance history requires reconciliation.");
        return rows.Count == 0 ? null : rows[0];
    }

    internal static async Task<bool> CanReserveAsync(ApplicationDbContext context,
        Guid userId, int requiredPoints, CancellationToken cancellationToken)
    {
        if (!await LockUserAsync(context, userId, cancellationToken))
            return false;
        var current = await LockBalanceValueAsync(context, userId, cancellationToken);
        if (current is null)
            return false;
        if (current.Value < 0)
            throw new ConflictException("The loyalty balance is negative and requires reconciliation.");
        var active = await OrderAmendmentLoyaltyReservationEvidence.ReadOutstandingAsync(
            context, userId, cancellationToken);
        return active <= int.MaxValue - requiredPoints && current.Value >= active + requiredPoints;
    }
}
