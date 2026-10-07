using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Common.Services;

internal static class RetainedLoyaltyEvidenceScrubber
{
    private sealed record ErasureScope(Guid[] OrderIds, Guid[] OwnerLinkIds);

    private const string Erased = "[erased]";
    private const string Audit = "RetainedLoyaltyEvidenceScrubber";

    internal static async Task ScrubAsync(ApplicationDbContext context, Guid userId,
        CancellationToken cancellationToken)
    {
        var scope = await LockAndValidateErasureScopeAsync(context, userId, cancellationToken);
        var now = DateTime.UtcNow;
        await AnonymizeCompensationAuditAsync(context, scope.OwnerLinkIds, now, cancellationToken);
        await AnonymizeSuppressionAuditAsync(context, scope.OrderIds, now, cancellationToken);
        var redemptionIds = await DeleteRedemptionSourcesAsync(
            context, userId, scope.OrderIds, cancellationToken);
        await AnonymizeOrderTransactionsAsync(
            context, userId, scope.OrderIds, redemptionIds, now, cancellationToken);
        await context.FidelityPointsTransactions.Where(value => value.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<ErasureScope> LockAndValidateErasureScopeAsync(
        ApplicationDbContext context, Guid userId, CancellationToken cancellationToken)
    {
        var relatedOrders = await LockRelatedOrdersAsync(context, userId, cancellationToken);
        var orderIds = relatedOrders.Select(value => value.OrderId).ToArray();
        var ownerLinkIds = await LockOwnerLinksAsync(context, userId, orderIds, cancellationToken);
        await EnsureNoActiveOwnerHoldsAsync(context, ownerLinkIds, cancellationToken);
        await LockUserRowAsync(context, userId, cancellationToken);
        await EnsureScopeIsUnchangedAsync(context, userId, relatedOrders, ownerLinkIds, cancellationToken);
        return new(orderIds, ownerLinkIds);
    }

    private static async Task<Guid[]> LockOwnerLinksAsync(
        ApplicationDbContext context, Guid userId, Guid[] orderIds, CancellationToken cancellationToken) =>
        orderIds.Length == 0 ? []
            : await context.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM order_billing_snapshot_owner_links
                WHERE user_id = {userId} AND order_id = ANY({orderIds})
                ORDER BY order_id, id FOR UPDATE
                """).ToArrayAsync(cancellationToken);

    private static async Task LockUserRowAsync(
        ApplicationDbContext context, Guid userId, CancellationToken cancellationToken)
    {
        var userRows = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM "Users" WHERE id = {userId} FOR UPDATE
            """).ToListAsync(cancellationToken);
        if (userRows.Count != 1)
            throw new ConflictException("The account changed while customer erasure was being prepared.");
    }

    private static async Task EnsureScopeIsUnchangedAsync(
        ApplicationDbContext context, Guid userId, IReadOnlyCollection<RelatedOrder> relatedOrders,
        Guid[] ownerLinkIds, CancellationToken cancellationToken)
    {
        var currentOrders = await ReadRelatedOrdersAsync(context, userId, cancellationToken);
        var currentOwners = await context.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .Where(value => value.UserId == userId).Select(value => value.Id).ToArrayAsync(cancellationToken);
        if (!SameMembership(relatedOrders, currentOrders) || currentOwners.Except(ownerLinkIds).Any())
            throw new ConflictException("New order loyalty evidence appeared during erasure; retry the deletion.");
        await EnsureNoActiveOwnerHoldsAsync(context, ownerLinkIds, cancellationToken);
    }

    private static async Task EnsureNoActiveOwnerHoldsAsync(
        ApplicationDbContext context, Guid[] ownerLinkIds, CancellationToken cancellationToken)
    {
        if (ownerLinkIds.Length > 0 && await context.OrderAmendmentLoyaltyOwnerHolds.AsNoTracking()
                .AnyAsync(value => ownerLinkIds.Contains(value.OwnerLinkId) && value.ReleasedAt == null,
                    cancellationToken))
            throw new ConflictException("Resolve the protected loyalty amendment before erasing this customer.");
    }

    private static async Task AnonymizeCompensationAuditAsync(
        ApplicationDbContext context, Guid[] ownerLinkIds, DateTime now, CancellationToken cancellationToken)
    {
        if (ownerLinkIds.Length == 0)
            return;
        var compensationIds = await context.OrderAmendmentLoyaltyCompensations.AsNoTracking()
            .Where(value => ownerLinkIds.Contains(value.OwnerLinkId))
            .Select(value => value.Id).ToArrayAsync(cancellationToken);
        await context.OrderAmendmentLoyaltyCompensations
            .Where(value => compensationIds.Contains(value.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.CreatedBy, Audit)
                .SetProperty(value => value.UpdatedAt, (DateTime?)now)
                .SetProperty(value => value.UpdatedBy, Audit), cancellationToken);
        await context.OrderAmendmentLoyaltyCompensationUnits
            .Where(value => compensationIds.Contains(value.CompensationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.CreatedBy, Audit)
                .SetProperty(value => value.UpdatedAt, (DateTime?)now)
                .SetProperty(value => value.UpdatedBy, Audit), cancellationToken);
        await context.OrderAmendmentLoyaltyCompensationPostings
            .Where(value => compensationIds.Contains(value.CompensationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.CreatedBy, Audit)
                .SetProperty(value => value.UpdatedAt, (DateTime?)now)
                .SetProperty(value => value.UpdatedBy, Audit), cancellationToken);
        await context.OrderAmendmentLoyaltyReservations
            .Where(value => ownerLinkIds.Contains(value.OwnerLinkId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.CreatedBy, Audit)
                .SetProperty(value => value.UpdatedAt, (DateTime?)now)
                .SetProperty(value => value.UpdatedBy, Audit), cancellationToken);
        await context.OrderAmendmentLoyaltyOwnerHolds
            .Where(value => ownerLinkIds.Contains(value.OwnerLinkId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.CreatedBy, Audit)
                .SetProperty(value => value.UpdatedAt, (DateTime?)now)
                .SetProperty(value => value.UpdatedBy, Audit), cancellationToken);
    }

    private static async Task AnonymizeSuppressionAuditAsync(
        ApplicationDbContext context, Guid[] orderIds, DateTime now, CancellationToken cancellationToken)
    {
        if (orderIds.Length == 0)
            return;
        await context.OrderBillingUnitAwardSuppressions.Where(value => orderIds.Contains(value.OrderId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.CreatedBy, Audit)
                .SetProperty(value => value.UpdatedAt, (DateTime?)now)
                .SetProperty(value => value.UpdatedBy, Audit), cancellationToken);
    }

    private static async Task<Guid[]> DeleteRedemptionSourcesAsync(
        ApplicationDbContext context, Guid userId, Guid[] orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Length == 0)
            return [];
        var redemptionIds = await context.OrderBillingSnapshots.AsNoTracking()
            .Where(value => orderIds.Contains(value.OrderId) && value.RedemptionTransactionId.HasValue)
            .Select(value => value.RedemptionTransactionId!.Value).ToArrayAsync(cancellationToken);
        if (redemptionIds.Length == 0)
            return [];
        // The published snapshot contract rejects a UserId update to its original debit. Deleting
        // that source row is allowed only in the same transaction as deleting its owner; the
        // deferred database trigger verifies its frozen facts and the erased owner at commit.
        await context.FidelityPointsTransactions
            .Where(value => redemptionIds.Contains(value.Id) && value.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        return redemptionIds;
    }

    private static async Task AnonymizeOrderTransactionsAsync(
        ApplicationDbContext context, Guid userId, Guid[] orderIds, Guid[] redemptionIds,
        DateTime now, CancellationToken cancellationToken)
    {
        if (orderIds.Length == 0)
            return;
        await context.FidelityPointsTransactions
            .Where(value => value.UserId == userId && value.OrderId.HasValue
                && orderIds.Contains(value.OrderId.Value)
                && !redemptionIds.Contains(value.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.UserId, (Guid?)null)
                .SetProperty(value => value.Description, Erased)
                .SetProperty(value => value.CreatedBy, Audit)
                .SetProperty(value => value.UpdatedAt, (DateTime?)now)
                .SetProperty(value => value.UpdatedBy, Audit), cancellationToken);
    }

    private static async Task<RelatedOrder[]> LockRelatedOrdersAsync(ApplicationDbContext context, Guid userId,
        CancellationToken cancellationToken)
    {
        var locatedOrders = await ReadRelatedOrdersAsync(context, userId, cancellationToken);
        if (locatedOrders.Length == 0) return [];

        var serviceSessionIds = locatedOrders.Where(value => value.ServiceSessionId.HasValue)
            .Select(value => value.ServiceSessionId!.Value).Distinct().Order().ToArray();
        if (serviceSessionIds.Length > 0)
        {
            var lockedSessions = await context.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM table_service_sessions
                WHERE id = ANY({serviceSessionIds}) ORDER BY id FOR UPDATE
                """).ToArrayAsync(cancellationToken);
            if (lockedSessions.Length != serviceSessionIds.Length
                || !serviceSessionIds.ToHashSet().SetEquals(lockedSessions))
                throw new ConflictException("The account's table-session evidence changed during customer erasure.");
        }

        var ids = locatedOrders.Select(value => value.OrderId).ToArray();
        var locked = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM orders
            WHERE id = ANY({ids}) ORDER BY id FOR UPDATE
            """).ToArrayAsync(cancellationToken);
        if (locked.Length != ids.Length || !ids.ToHashSet().SetEquals(locked))
            throw new ConflictException("The account's order evidence changed during customer erasure.");

        var current = await ReadRelatedOrdersAsync(context, userId, cancellationToken);
        if (!SameMembership(locatedOrders, current))
            throw new ConflictException("The account's table-session membership changed during customer erasure.");
        return current;
    }

    private static async Task<RelatedOrder[]> ReadRelatedOrdersAsync(ApplicationDbContext context, Guid userId,
        CancellationToken cancellationToken)
    {
        var rows = await context.Orders.IgnoreQueryFilters().AsNoTracking()
            // soft-delete-bypass: deleted orders retain financial and loyalty evidence that must be locked and scrubbed.
            .Where(order => order.UserId == userId || context.OrderBillingSnapshotOwnerLinks
                .Any(owner => owner.OrderId == order.Id && owner.UserId == userId))
            .OrderBy(order => order.Id)
            .Select(order => new { order.Id, order.ServiceSessionId })
            .ToArrayAsync(cancellationToken);
        return rows.Select(value => new RelatedOrder(value.Id, value.ServiceSessionId)).ToArray();
    }

    private static bool SameMembership(IReadOnlyCollection<RelatedOrder> left,
        IReadOnlyCollection<RelatedOrder> right) => left.Count == right.Count
        && left.OrderBy(value => value.OrderId).SequenceEqual(right.OrderBy(value => value.OrderId));

    private sealed record RelatedOrder(Guid OrderId, Guid? ServiceSessionId);
}
