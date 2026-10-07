using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentLoyaltyReservationEvidence
{
    internal static async Task<long> ReadOutstandingAsync(ApplicationDbContext context,
        Guid userId, CancellationToken cancellationToken) => await context.Database.SqlQuery<long>($"""
            SELECT COALESCE(SUM(c.required_points), 0)::bigint AS "Value"
            FROM order_amendment_loyalty_reservations r
            JOIN order_amendment_loyalty_compensations c
              ON c.source_order_id = r.source_order_id AND c.id = r.compensation_id
            JOIN order_billing_snapshot_owner_links l
              ON l.order_id = r.source_order_id AND l.id = r.owner_link_id
            WHERE l.user_id = {userId} AND l.disposition = 'Linked'
            AND r.state IN ('HeldShortfall', 'Reserved')
            """).SingleAsync(cancellationToken);

    internal static async Task<long?> ReadAvailableBeforeReservationAsync(ApplicationDbContext context,
        OrderAmendmentLoyaltyReservation reservation, int requiredPoints,
        CancellationToken cancellationToken)
    {
        var owner = await context.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .Where(value => value.Id == reservation.OwnerLinkId && value.OrderId == reservation.SourceOrderId)
            .Select(value => new { value.UserId, value.Disposition, value.ErasedAt, value.ErasureTransactionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (owner is null || owner.Disposition != OrderBillingSnapshotOwnerDisposition.Linked
            || owner.UserId is not Guid userId || owner.ErasedAt.HasValue || owner.ErasureTransactionId is not null)
            return null;
        var current = await context.FidelityPointBalances.AsNoTracking().Where(value => value.UserId == userId)
            .Select(value => (int?)value.CurrentPoints).SingleOrDefaultAsync(cancellationToken) ?? 0;
        if (current < 0)
            throw new ConflictException("The loyalty balance is negative and requires reconciliation.");
        var active = await ReadOutstandingAsync(context, userId, cancellationToken);
        if (active < requiredPoints)
            throw new ConflictException("The exact loyalty reservation is missing from the active obligation total.");
        return Math.Max(0L, (long)current - (active - requiredPoints));
    }

    internal static async Task<bool> AreRequiredOwnersAvailableAsync(ApplicationDbContext context,
        Guid operationId, CancellationToken cancellationToken)
    {
        var operation = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Where(value => value.Id == operationId)
            .Select(value => new { value.SourceOrderId, value.SnapshotJson }).SingleOrDefaultAsync(cancellationToken);
        if (operation is null)
            throw new ConflictException("The loyalty resolution operation is unavailable.");
        OrderAmendmentResolutionSnapshot snapshot;
        try { snapshot = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionSnapshot>(operation.SnapshotJson); }
        catch (JsonException exception)
        { throw new ConflictException("The frozen loyalty owner authority is unavailable.", exception); }
        var plan = snapshot.LoyaltyPlan;
        if (plan?.SnapshotId is null)
            return true;
        var ids = new[] { plan.EarningOwnerLinkId, plan.RedemptionOwnerLinkId }
            .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        if (ids.Length == 0)
            return true;
        var links = await context.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .Where(value => value.OrderId == operation.SourceOrderId && ids.Contains(value.Id))
            .ToListAsync(cancellationToken);
        return links.Count == ids.Length && links.All(value =>
            value.Disposition == OrderBillingSnapshotOwnerDisposition.Linked && value.UserId.HasValue
            && !value.ErasedAt.HasValue && value.ErasureTransactionId is null);
    }

    internal static async Task ReleaseOwnerHoldsAsync(ApplicationDbContext context,
        OrderAmendmentResolutionOperation operation, DateTime now, CancellationToken cancellationToken)
    {
        if (operation.State != OrderAmendmentResolutionOperationState.Resolved)
            throw new ConflictException("Loyalty owner protection cannot be released before settlement resolves.");
        var holds = await context.OrderAmendmentLoyaltyOwnerHolds
            .Where(value => value.OperationId == operation.Id && value.SourceOrderId == operation.SourceOrderId
                && value.ReleasedAt == null).ToListAsync(cancellationToken);
        foreach (var hold in holds)
        {
            hold.ReleasedAt = now;
            hold.UpdatedAt = now;
            hold.UpdatedBy = OrderAmendmentLoyaltyAudit.Identifier;
        }
    }
}
