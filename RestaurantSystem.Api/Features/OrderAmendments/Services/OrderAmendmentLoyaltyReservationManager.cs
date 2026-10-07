using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentLoyaltyReservationManager
{
    internal static async Task InitializeAsync(ApplicationDbContext context,
        OrderAmendmentResolutionOperation operation, OrderAmendmentLoyaltyPlan? plan,
        DateTime now, string audit, CancellationToken cancellationToken)
    {
        if (plan is null || plan.SnapshotId is null)
            return;
        var ownerLinks = await OrderAmendmentLoyaltyOwnerLinks.LockForOrderAsync(
            context, operation.SourceOrderId, cancellationToken);
        var holdOwnerIds = plan.Compensations.Select(value => value.OwnerLinkId)
            .Concat(plan.PendingAwardSuppressions.Count > 0 && plan.EarningOwnerLinkId.HasValue
                ? [plan.EarningOwnerLinkId.Value] : [])
            .Distinct().Order().ToArray();
        foreach (var ownerLinkId in holdOwnerIds)
        {
            var owner = ownerLinks.SingleOrDefault(value => value.Id == ownerLinkId);
            if (owner is null || owner.Disposition != OrderBillingSnapshotOwnerDisposition.Linked
                || owner.UserId is not Guid ownerId || owner.ErasedAt.HasValue
                || owner.ErasureTransactionId is not null
                || !await OrderAmendmentLoyaltyBalanceLocks.LockUserAsync(context, ownerId, cancellationToken))
                throw new ConflictException("The loyalty owner is unavailable for a protected amendment obligation.");
            context.OrderAmendmentLoyaltyOwnerHolds.Add(new OrderAmendmentLoyaltyOwnerHold
            {
                Id = Guid.NewGuid(),
                SourceOrderId = operation.SourceOrderId,
                OperationId = operation.Id,
                OwnerLinkId = ownerLinkId,
                CreatedAt = now,
                CreatedBy = audit
            });
        }
        foreach (var suppression in plan.PendingAwardSuppressions)
        {
            context.OrderBillingUnitAwardSuppressions.Add(new OrderBillingUnitAwardSuppression
            {
                Id = Guid.NewGuid(),
                OrderId = operation.SourceOrderId,
                SnapshotUnitId = suppression.SnapshotUnitId,
                AmendmentId = operation.AmendmentId,
                SuppressedEarnedPoints = suppression.EarnedPoints,
                CreatedAt = now,
                CreatedBy = audit
            });
        }

        foreach (var item in plan.Compensations)
        {
            var header = new OrderAmendmentLoyaltyCompensation
            {
                Id = Guid.NewGuid(),
                SourceOrderId = operation.SourceOrderId,
                AmendmentId = operation.AmendmentId,
                SnapshotId = item.SnapshotId,
                OwnerLinkId = item.OwnerLinkId,
                OriginalTransactionId = item.OriginalTransactionId,
                AwardWitnessId = item.AwardWitnessId,
                OperationId = operation.Id,
                Kind = item.Kind,
                OriginalTransactionPoints = item.OriginalTransactionPoints,
                RequiredPoints = item.RequiredPoints,
                PlanFingerprint = item.PlanFingerprint,
                CreatedAt = now,
                CreatedBy = audit
            };
            context.OrderAmendmentLoyaltyCompensations.Add(header);
            context.OrderAmendmentLoyaltyCompensationUnits.AddRange(item.Units.Select(unit =>
                new OrderAmendmentLoyaltyCompensationUnit
                {
                    Id = Guid.NewGuid(),
                    CompensationId = header.Id,
                    SourceOrderId = operation.SourceOrderId,
                    SnapshotUnitId = unit.SnapshotUnitId,
                    Kind = item.Kind,
                    Points = unit.Points,
                    CreatedAt = now,
                    CreatedBy = audit
                }));

            if (item.Kind != OrderAmendmentLoyaltyCompensationKind.EarnedClawback)
                continue;
            var owner = ownerLinks.SingleOrDefault(value => value.Id == item.OwnerLinkId);
            if (owner is null || owner.Slot != OrderBillingSnapshotOwnerSlot.Earning
                || owner.Disposition != OrderBillingSnapshotOwnerDisposition.Linked
                || !owner.UserId.HasValue || owner.ErasedAt.HasValue
                || owner.ErasureTransactionId is not null)
                throw new ConflictException("The original earning owner is unavailable for a protected clawback.");
            var state = await OrderAmendmentLoyaltyBalanceLocks.CanReserveAsync(context, owner.UserId.Value,
                item.RequiredPoints, cancellationToken)
                ? OrderAmendmentLoyaltyReservationState.Reserved
                : OrderAmendmentLoyaltyReservationState.HeldShortfall;
            context.OrderAmendmentLoyaltyReservations.Add(new OrderAmendmentLoyaltyReservation
            {
                Id = Guid.NewGuid(),
                SourceOrderId = operation.SourceOrderId,
                OperationId = operation.Id,
                CompensationId = header.Id,
                OwnerLinkId = item.OwnerLinkId,
                State = state,
                CreatedAt = now,
                CreatedBy = audit
            });
        }
    }

    internal static async Task<bool> TryActivateHeldAsync(ApplicationDbContext context,
        Guid operationId, CancellationToken cancellationToken)
    {
        var sourceOrderId = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Where(value => value.Id == operationId)
            .Select(value => (Guid?)value.SourceOrderId).SingleOrDefaultAsync(cancellationToken);
        if (!sourceOrderId.HasValue)
            throw new ConflictException("The loyalty reservation operation is unavailable.");

        await using var scope = await OrderAccountMutationScope.BeginAsync(
            context, sourceOrderId.Value, cancellationToken);
        var ownerLinks = await OrderAmendmentLoyaltyOwnerLinks.LockForOrderAsync(
            context, sourceOrderId.Value, cancellationToken);
        var reservation = await context.OrderAmendmentLoyaltyReservations
            .SingleOrDefaultAsync(value => value.OperationId == operationId, cancellationToken);
        if (reservation is null)
        {
            await scope.CommitAsync(cancellationToken);
            return true;
        }
        if (reservation.State is not (OrderAmendmentLoyaltyReservationState.HeldShortfall
                or OrderAmendmentLoyaltyReservationState.Released))
        {
            await scope.CommitAsync(cancellationToken);
            return reservation.State is OrderAmendmentLoyaltyReservationState.Reserved
                or OrderAmendmentLoyaltyReservationState.Consumed;
        }

        var owner = ownerLinks.SingleOrDefault(value => value.Id == reservation.OwnerLinkId);
        if (owner is null || owner.Disposition != OrderBillingSnapshotOwnerDisposition.Linked
            || !owner.UserId.HasValue || owner.ErasedAt.HasValue || owner.ErasureTransactionId is not null
            || !await OrderAmendmentLoyaltyBalanceLocks.LockUserAsync(context, owner.UserId.Value, cancellationToken))
            return false;
        var current = await OrderAmendmentLoyaltyBalanceLocks.LockBalanceValueAsync(
            context, owner.UserId.Value, cancellationToken);
        if (current is null)
            return false;
        var active = await OrderAmendmentLoyaltyReservationEvidence.ReadOutstandingAsync(
            context, owner.UserId.Value, cancellationToken);
        if (current.Value < 0)
            throw new ConflictException("The loyalty balance is negative and requires reconciliation.");
        var required = await context.OrderAmendmentLoyaltyCompensations.AsNoTracking()
            .Where(value => value.Id == reservation.CompensationId
                && value.SourceOrderId == sourceOrderId.Value)
            .Select(value => (long?)value.RequiredPoints).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ConflictException("The reserved loyalty compensation header is unavailable.");
        if (reservation.State == OrderAmendmentLoyaltyReservationState.Released)
            active = checked(active + required);
        if (active > current.Value)
        {
            if (reservation.State == OrderAmendmentLoyaltyReservationState.Released)
            {
                reservation.State = OrderAmendmentLoyaltyReservationState.HeldShortfall;
                reservation.UpdatedAt = DateTime.UtcNow;
                reservation.UpdatedBy = OrderAmendmentLoyaltyAudit.Identifier;
                await context.SaveChangesAsync(cancellationToken);
                await scope.CommitAsync(cancellationToken);
            }
            return false;
        }

        reservation.State = OrderAmendmentLoyaltyReservationState.Reserved;
        reservation.UpdatedAt = DateTime.UtcNow;
        reservation.UpdatedBy = OrderAmendmentLoyaltyAudit.Identifier;
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return true;
    }

    internal static async Task<bool> IsReservedUnderOrderLockAsync(ApplicationDbContext context,
        Guid operationId, CancellationToken cancellationToken)
    {
        var state = await context.OrderAmendmentLoyaltyReservations.AsNoTracking()
            .Where(value => value.OperationId == operationId)
            .Select(value => (OrderAmendmentLoyaltyReservationState?)value.State)
            .SingleOrDefaultAsync(cancellationToken);
        return state is null or OrderAmendmentLoyaltyReservationState.Reserved
            or OrderAmendmentLoyaltyReservationState.Consumed;
    }

}

internal static class OrderAmendmentLoyaltyAudit
{
    internal const string Identifier = "OrderAmendmentLoyaltyCompensation";
}
