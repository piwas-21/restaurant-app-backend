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
        var holdOwnerIds = ReadHoldOwnerIds(plan);
        await AddOwnerHoldsAsync(context, operation, ownerLinks, holdOwnerIds, now, audit, cancellationToken);
        AddSuppressionRows(context, operation, plan.PendingAwardSuppressions, now, audit);
        await AddCompensationRowsAsync(
            context, operation, ownerLinks, plan.Compensations, now, audit, cancellationToken);
    }

    private static Guid[] ReadHoldOwnerIds(OrderAmendmentLoyaltyPlan plan) =>
        plan.Compensations.Select(value => value.OwnerLinkId)
            .Concat(plan.PendingAwardSuppressions.Count > 0 && plan.EarningOwnerLinkId.HasValue
                ? [plan.EarningOwnerLinkId.Value] : [])
            .Distinct().Order().ToArray();

    private static async Task AddOwnerHoldsAsync(
        ApplicationDbContext context, OrderAmendmentResolutionOperation operation,
        IReadOnlyList<OrderBillingSnapshotOwnerLink> ownerLinks, IReadOnlyList<Guid> holdOwnerIds,
        DateTime now, string audit, CancellationToken cancellationToken)
    {
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
    }

    private static void AddSuppressionRows(
        ApplicationDbContext context, OrderAmendmentResolutionOperation operation,
        IReadOnlyList<OrderAmendmentLoyaltyUnitAllocation> suppressions, DateTime now, string audit)
    {
        foreach (var suppression in suppressions)
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

    private static async Task AddCompensationRowsAsync(
        ApplicationDbContext context, OrderAmendmentResolutionOperation operation,
        IReadOnlyList<OrderBillingSnapshotOwnerLink> ownerLinks,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationPlan> compensations,
        DateTime now, string audit, CancellationToken cancellationToken)
    {
        foreach (var item in compensations)
            await AddCompensationRowAsync(
                context, operation, ownerLinks, item, now, audit, cancellationToken);
    }

    private static async Task AddCompensationRowAsync(
        ApplicationDbContext context, OrderAmendmentResolutionOperation operation,
        IReadOnlyList<OrderBillingSnapshotOwnerLink> ownerLinks,
        OrderAmendmentLoyaltyCompensationPlan item, DateTime now, string audit,
        CancellationToken cancellationToken)
    {
        var header = NewCompensationHeader(operation, item, now, audit);
        context.OrderAmendmentLoyaltyCompensations.Add(header);
        context.OrderAmendmentLoyaltyCompensationUnits.AddRange(item.Units.Select(unit =>
            NewCompensationUnit(operation.SourceOrderId, header.Id, item.Kind, unit, now, audit)));
        if (item.Kind != OrderAmendmentLoyaltyCompensationKind.EarnedClawback)
            return;
        var owner = RequireEarningOwner(ownerLinks, item.OwnerLinkId);
        var state = await OrderAmendmentLoyaltyBalanceLocks.CanReserveAsync(
                context, owner.UserId!.Value, item.RequiredPoints, cancellationToken)
            ? OrderAmendmentLoyaltyReservationState.Reserved
            : OrderAmendmentLoyaltyReservationState.HeldShortfall;
        context.OrderAmendmentLoyaltyReservations.Add(NewReservation(operation, header, item, state, now, audit));
    }

    private static OrderAmendmentLoyaltyCompensation NewCompensationHeader(
        OrderAmendmentResolutionOperation operation, OrderAmendmentLoyaltyCompensationPlan item,
        DateTime now, string audit) => new()
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

    private static OrderAmendmentLoyaltyCompensationUnit NewCompensationUnit(
        Guid sourceOrderId, Guid compensationId, OrderAmendmentLoyaltyCompensationKind kind,
        OrderAmendmentLoyaltyCompensationUnitPlan unit, DateTime now, string audit) => new()
        {
            Id = Guid.NewGuid(),
            CompensationId = compensationId,
            SourceOrderId = sourceOrderId,
            SnapshotUnitId = unit.SnapshotUnitId,
            Kind = kind,
            Points = unit.Points,
            CreatedAt = now,
            CreatedBy = audit
        };

    private static OrderBillingSnapshotOwnerLink RequireEarningOwner(
        IReadOnlyList<OrderBillingSnapshotOwnerLink> ownerLinks, Guid ownerLinkId)
    {
        var owner = ownerLinks.SingleOrDefault(value => value.Id == ownerLinkId);
        if (owner is null || owner.Slot != OrderBillingSnapshotOwnerSlot.Earning
            || owner.Disposition != OrderBillingSnapshotOwnerDisposition.Linked
            || !owner.UserId.HasValue || owner.ErasedAt.HasValue || owner.ErasureTransactionId is not null)
            throw new ConflictException("The original earning owner is unavailable for a protected clawback.");
        return owner;
    }

    private static OrderAmendmentLoyaltyReservation NewReservation(
        OrderAmendmentResolutionOperation operation, OrderAmendmentLoyaltyCompensation header,
        OrderAmendmentLoyaltyCompensationPlan item, OrderAmendmentLoyaltyReservationState state,
        DateTime now, string audit) => new()
        {
            Id = Guid.NewGuid(),
            SourceOrderId = operation.SourceOrderId,
            OperationId = operation.Id,
            CompensationId = header.Id,
            OwnerLinkId = item.OwnerLinkId,
            State = state,
            CreatedAt = now,
            CreatedBy = audit
        };

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
