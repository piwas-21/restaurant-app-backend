using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed class OrderBillingAwardSuppressionWriter(ApplicationDbContext context)
    : IOrderBillingAwardSuppressionWriter
{
    private const string AuditIdentifier = "OrderBillingAwardSuppression";

    public async Task RecordRemovedUnitsAsync(
        Guid orderId, Guid amendmentId, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new ConflictException("Loyalty unit suppression requires the accepted amendment transaction.");

        var lockedOrders = await context.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM orders WHERE id = {orderId} AND is_deleted = FALSE FOR UPDATE")
            .Take(2)
            .ToListAsync(cancellationToken);
        if (lockedOrders.Count != 1 || lockedOrders[0] != orderId)
            throw new ConflictException("The source order is unavailable for loyalty unit suppression.");
        var orderPreviewPoints = await context.Orders.AsNoTracking()
            .Where(value => value.Id == orderId)
            .Select(value => value.FidelityPointsEarned)
            .SingleAsync(cancellationToken);

        var amendment = await context.OrderAmendments.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == amendmentId, cancellationToken)
            ?? throw new ConflictException("The accepted loyalty suppression amendment is unavailable.");
        if (amendment.SourceOrderId != orderId || amendment.State != OrderAmendmentState.Committed)
            throw new ConflictException("Loyalty unit suppression requires a committed source-order amendment.");

        List<OrderAmendmentChangeSnapshot> changes;
        try
        {
            changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ConflictException("The committed amendment has invalid removal-scope evidence.");
        }
        if (changes.Any(change => change is null))
            throw new ConflictException("The committed amendment contains an empty removal-scope entry.");

        var removals = changes.Where(change =>
            change.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace).ToArray();
        if (removals.Length == 0)
            return;

        var snapshot = await context.OrderBillingSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrderId == orderId, cancellationToken);
        if (snapshot is null)
            return;
        if (!snapshot.EarnedPointsCandidate.HasValue)
        {
            if (orderPreviewPoints != 0 || HasPartialEarningEvidence(snapshot))
                throw new ConflictException("The unevaluated earning snapshot contains partial rule evidence.");
            return;
        }
        if (snapshot.EarnedPointsCandidate < 0 || snapshot.EarnedPointsCandidate != orderPreviewPoints)
            throw new ConflictException("The accepted earning candidate does not match the source-order preview.");

        if (await context.OrderBillingAwardWitnesses.AsNoTracking()
                .AnyAsync(value => value.OrderId == orderId, cancellationToken)
            || await context.FidelityPointsTransactions.AsNoTracking().AnyAsync(value =>
                value.OrderId == orderId && value.TransactionType == TransactionType.Earned, cancellationToken))
            return;
        if (snapshot.EarnedPointsCandidate == 0)
            return;

        var units = await context.OrderBillingSnapshotUnits.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .OrderBy(value => value.OrderItemId)
            .ThenBy(value => value.UnitOrdinal)
            .ToListAsync(cancellationToken);
        ValidateFrozenAllocation(snapshot.EarnedPointsCandidate.Value, units);

        var selected = new Dictionary<Guid, OrderBillingSnapshotUnit>();
        foreach (var change in removals)
        {
            if (change.OrderItemId == Guid.Empty || change.StartOrdinal < 1 || change.Quantity < 1)
                throw new ConflictException("The committed amendment contains an invalid earning-unit range.");
            var end = (long)change.StartOrdinal + change.Quantity;
            var range = units.Where(unit => unit.OrderItemId == change.OrderItemId
                && unit.UnitOrdinal >= change.StartOrdinal && unit.UnitOrdinal < end).ToArray();
            if (range.Length != change.Quantity || range.Any(unit => !selected.TryAdd(unit.Id, unit)))
                throw new ConflictException("The amendment removal ranges overlap or do not match frozen earning units.");
        }

        var affected = selected.Values.Where(value => value.EarnedPoints > 0).ToArray();
        if (affected.Length == 0)
            return;

        var selectedIds = affected.Select(value => value.Id).ToArray();
        if (await context.OrderBillingUnitAwardSuppressions.AsNoTracking()
                .AnyAsync(value => selectedIds.Contains(value.SnapshotUnitId), cancellationToken))
            throw new ConflictException("An accepted removal unit already has loyalty suppression history.");

        var now = DateTime.UtcNow;
        context.OrderBillingUnitAwardSuppressions.AddRange(affected.Select(unit => new OrderBillingUnitAwardSuppression
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            SnapshotUnitId = unit.Id,
            AmendmentId = amendmentId,
            SuppressedEarnedPoints = unit.EarnedPoints,
            CreatedAt = now,
            CreatedBy = AuditIdentifier
        }));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateFrozenAllocation(
        int candidate, IReadOnlyCollection<OrderBillingSnapshotUnit> units)
    {
        long total = 0;
        foreach (var unit in units)
        {
            if (unit.EarnedPoints < 0 || unit.UnitOrdinal < 1)
                throw new ConflictException("The frozen earning units contain invalid values.");
            total = checked(total + unit.EarnedPoints);
        }
        if (total != candidate)
            throw new ConflictException("The frozen earning units do not sum to the accepted candidate.");
    }

    private static bool HasPartialEarningEvidence(OrderBillingSnapshot snapshot) =>
        snapshot.EarningEvaluationVersion is not null
        || snapshot.EarningRuleSetFingerprint is not null
        || snapshot.EarningRuleId.HasValue
        || snapshot.EarningRuleName is not null
        || snapshot.EarningRuleMinimumMinor.HasValue
        || snapshot.EarningRuleMaximumMinor.HasValue
        || snapshot.EarningRulePoints.HasValue
        || snapshot.EarningRulePriority.HasValue;
}
