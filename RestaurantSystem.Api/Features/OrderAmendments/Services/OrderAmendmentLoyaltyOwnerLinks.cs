using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentLoyaltyOwnerLinks
{
    internal static async Task<IReadOnlyList<OrderBillingSnapshotOwnerLink>> LockForOrderAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        var ids = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM order_billing_snapshot_owner_links
            WHERE order_id = {orderId} ORDER BY id FOR UPDATE
            """)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
            return [];
        return await context.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .Where(value => value.OrderId == orderId && ids.Contains(value.Id))
            .OrderBy(value => value.Id).ToListAsync(cancellationToken);
    }
}
