using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentLoyaltyCompensationPoster
{
    internal static async Task ApplyAsync(ApplicationDbContext context,
        OrderAmendmentResolutionOperation operation, OrderAmendmentLoyaltyPlan? plan,
        OrderAmendmentLoyaltyEvidence evidence, DateTime now, string audit,
        CancellationToken cancellationToken)
    {
        if (plan is null || plan.SnapshotId is null)
        {
            if (await context.OrderAmendmentLoyaltyCompensations.AnyAsync(
                    value => value.OperationId == operation.Id, cancellationToken)
                || evidence.Suppressions.Any(value => value.AmendmentId == operation.AmendmentId))
                throw Invalid();
            return;
        }

        var lockedOwnerLinks = await OrderAmendmentLoyaltyOwnerLinks.LockForOrderAsync(
            context, operation.SourceOrderId, cancellationToken);
        var rows = await context.OrderAmendmentLoyaltyCompensations
            .Where(value => value.OperationId == operation.Id).ToArrayAsync(cancellationToken);
        if (rows.Length != plan.Compensations.Count)
            throw Invalid();
        var ownerLinks = lockedOwnerLinks.ToDictionary(value => value.Id);
        var originals = evidence.Transactions.ToDictionary(value => value.Id);
        var units = evidence.Units.ToDictionary(value => value.Id);
        var totalClawback = 0L;
        var totalRestoration = 0L;
        var ownerIds = new HashSet<Guid>();
        var matchByPlan = new List<(OrderAmendmentLoyaltyCompensation Header,
            OrderAmendmentLoyaltyCompensationPlan Plan)>();

        foreach (var item in plan.Compensations)
        {
            var header = rows.SingleOrDefault(value => value.AmendmentId == operation.AmendmentId
                && value.OriginalTransactionId == item.OriginalTransactionId && value.Kind == item.Kind)
                ?? throw Invalid();
            if (header.SourceOrderId != operation.SourceOrderId || header.SnapshotId != item.SnapshotId
                || header.OwnerLinkId != item.OwnerLinkId || header.AwardWitnessId != item.AwardWitnessId
                || header.OperationId != operation.Id || header.OriginalTransactionPoints != item.OriginalTransactionPoints
                || header.RequiredPoints != item.RequiredPoints || header.PlanFingerprint != item.PlanFingerprint
                || !ownerLinks.TryGetValue(header.OwnerLinkId, out var owner)
                || !evidence.OwnerLinks.Any(value => value.Id == owner.Id
                    && value.UserId == owner.UserId && value.Disposition == owner.Disposition)
                || owner.OrderId != operation.SourceOrderId || owner.Disposition != OrderBillingSnapshotOwnerDisposition.Linked
                || owner.UserId is not Guid userId || owner.ErasedAt.HasValue
                || owner.ErasureTransactionId is not null
                || !originals.TryGetValue(header.OriginalTransactionId, out var original)
                || original.OrderId != operation.SourceOrderId || original.UserId != userId
                || original.Points != header.OriginalTransactionPoints)
                throw Invalid();
            ownerIds.Add(userId);
            var storedUnits = await context.OrderAmendmentLoyaltyCompensationUnits.AsNoTracking()
                .Where(value => value.CompensationId == header.Id)
                .ToArrayAsync(cancellationToken);
            if (!StoredUnitsMatch(storedUnits, operation.SourceOrderId, item.Kind,
                    item.Units, units.Keys.ToHashSet())
                || storedUnits.Sum(value => (long)value.Points) != item.RequiredPoints)
                throw Invalid();
            if (item.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback)
                totalClawback = checked(totalClawback + item.RequiredPoints);
            else
                totalRestoration = checked(totalRestoration + item.RequiredPoints);
            matchByPlan.Add((header, item));
        }

        if (ownerIds.Count > 1)
            throw new ConflictException("The source order has conflicting immutable loyalty owners.");
        var currentSuppressions = evidence.Suppressions.Where(value => value.AmendmentId == operation.AmendmentId
                && value.OrderId == operation.SourceOrderId)
            .Select(value => value.SnapshotUnitId).Order().ToArray();
        var expectedSuppressedUnits = plan.AwardPending
            ? plan.RemovedUnits.Where(value => value.EarnedPoints > 0)
            : Array.Empty<OrderAmendmentLoyaltyUnitAllocation>();
        var expectedSuppressions = expectedSuppressedUnits
            .Select(value => value.SnapshotUnitId).Order().ToArray();
        if (!currentSuppressions.SequenceEqual(expectedSuppressions))
            throw Invalid();

        if (totalClawback == 0 && totalRestoration == 0)
            return;
        var headerIds = rows.Select(value => value.Id).ToArray();
        if (await context.OrderAmendmentLoyaltyCompensationPostings.AsNoTracking()
            .AnyAsync(value => headerIds.Contains(value.CompensationId), cancellationToken))
            throw Invalid();
        if (ownerIds.Count != 1)
            throw Invalid();
        if (!await OrderAmendmentLoyaltyBalanceLocks.LockUserAsync(
                context, ownerIds.Single(), cancellationToken))
            throw new ConflictException("The source loyalty owner is unavailable for exact compensation.");
        var balance = await LockTrackedBalanceAsync(context, ownerIds.Single(), cancellationToken);
        if (balance.CurrentPoints < 0)
            throw new ConflictException("The loyalty balance is negative and requires reconciliation.");
        var active = await OrderAmendmentLoyaltyReservationEvidence.ReadOutstandingAsync(
            context, ownerIds.Single(), cancellationToken);
        if (active < totalClawback || totalClawback > balance.CurrentPoints
            || totalClawback > 0 && balance.CurrentPoints < active)
            throw new ConflictException("The exact loyalty point obligation is no longer fully reserved.");
        var remainingObligations = active - totalClawback;
        var afterClawback = checked(balance.CurrentPoints - checked((int)totalClawback));
        if (totalClawback > 0 && afterClawback < remainingObligations)
            throw new ConflictException("The clawback would consume points reserved for another exact obligation.");

        foreach (var entry in matchByPlan)
            AddPosting(context, operation, entry.Header, entry.Plan, originals[entry.Header.OriginalTransactionId],
                ownerIds.Single(), now, audit);
        balance.CurrentPoints = checked(afterClawback + checked((int)totalRestoration));
        balance.LastUpdated = now;
        balance.UpdatedAt = now;
        balance.UpdatedBy = audit;

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

    private static void AddPosting(ApplicationDbContext context,
        OrderAmendmentResolutionOperation operation,
        OrderAmendmentLoyaltyCompensation header,
        OrderAmendmentLoyaltyCompensationPlan plan,
        FidelityPointsTransaction original, Guid userId, DateTime now, string audit)
    {
        var clawback = plan.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback;
        var delta = clawback ? checked(-plan.RequiredPoints) : plan.RequiredPoints;
        var movement = new FidelityPointsTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderId = operation.SourceOrderId,
            TransactionType = clawback ? TransactionType.EarnedClawback : TransactionType.RedemptionRestored,
            Points = delta,
            OriginalTransactionId = plan.OriginalTransactionId,
            OrderTotal = original.OrderTotal,
            Description = clawback
                ? "Earned points reversed for accepted order amendment"
                : "Redeemed points restored for accepted order amendment",
            CreatedAt = now,
            CreatedBy = audit
        };
        context.FidelityPointsTransactions.Add(movement);
        context.OrderAmendmentLoyaltyCompensationPostings.Add(new OrderAmendmentLoyaltyCompensationPosting
        {
            Id = Guid.NewGuid(),
            CompensationId = header.Id,
            MovementTransactionId = movement.Id,
            PointsDelta = delta,
            PostedAt = now,
            CreatedAt = now,
            CreatedBy = audit
        });
    }

    internal static bool StoredUnitsMatch(
        IReadOnlyCollection<OrderAmendmentLoyaltyCompensationUnit> storedUnits,
        Guid sourceOrderId, OrderAmendmentLoyaltyCompensationKind kind,
        IReadOnlyCollection<OrderAmendmentLoyaltyCompensationUnitPlan> expectedUnits,
        IReadOnlySet<Guid> acceptedUnitIds)
    {
        if (expectedUnits.Select(value => value.SnapshotUnitId).Distinct().Count() != expectedUnits.Count)
            return false;
        var expectedByUnit = expectedUnits.ToDictionary(value => value.SnapshotUnitId);
        return storedUnits.Count == expectedByUnit.Count
            && storedUnits.Select(value => value.SnapshotUnitId).Distinct().Count() == storedUnits.Count
            && storedUnits.All(value => value.SourceOrderId == sourceOrderId && value.Kind == kind
                && acceptedUnitIds.Contains(value.SnapshotUnitId)
                && expectedByUnit.TryGetValue(value.SnapshotUnitId, out var expected)
                && value.Points == expected.Points);
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

    private static ConflictException Invalid() => new(
        "The persisted loyalty compensation differs from its reviewed exact unit plan.");
}
