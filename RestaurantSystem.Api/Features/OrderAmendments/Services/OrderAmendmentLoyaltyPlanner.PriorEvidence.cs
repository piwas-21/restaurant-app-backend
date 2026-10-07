using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentLoyaltyPlanner
{
    private static HashSet<Guid> ValidatePriorCompensation(
        Guid sourceOrderId, Guid snapshotId, IReadOnlyList<OrderBillingSnapshotUnit> units,
        OrderAmendmentLoyaltyEvidence evidence)
    {
        if (evidence.PriorCompensations.Count == 0)
        {
            if (evidence.PriorUnits.Count != 0 || evidence.PriorPostings.Count != 0
                || evidence.Reservations.Count != 0)
                throw Held("Loyalty compensation details exist without their immutable header.");
            return [];
        }

        var unitsById = units.ToDictionary(value => value.Id);
        var amendments = evidence.CommittedAmendments.ToDictionary(value => value.Id);
        var operations = evidence.Operations.ToDictionary(value => value.Id);
        var originalRows = evidence.Transactions.ToDictionary(value => value.Id);
        var coverageByUnit = evidence.AwardCoverage.ToDictionary(value => value.SnapshotUnitId);
        var links = evidence.OwnerLinks.ToDictionary(value => value.Id);
        var compensationIds = evidence.PriorCompensations.Select(value => value.Id).ToHashSet();
        if (evidence.PriorUnits.Any(value => !compensationIds.Contains(value.CompensationId))
            || evidence.PriorPostings.Any(value => !compensationIds.Contains(value.CompensationId))
            || evidence.Reservations.Any(value => !compensationIds.Contains(value.CompensationId)))
            throw Held("Loyalty compensation details reference an unknown header.");

        var compensatedEarnedUnits = new HashSet<Guid>();
        var usedUnitKinds = new HashSet<(Guid UnitId, OrderAmendmentLoyaltyCompensationKind Kind)>();
        foreach (var header in evidence.PriorCompensations)
        {
            if (header.SourceOrderId != sourceOrderId || header.SnapshotId != snapshotId
                || header.Id == Guid.Empty || header.AmendmentId == Guid.Empty
                || header.OperationId == Guid.Empty || header.RequiredPoints <= 0
                || !amendments.TryGetValue(header.AmendmentId, out var amendment)
                || amendment.State != OrderAmendmentState.Committed
                || !operations.TryGetValue(header.OperationId, out var operation)
                || operation.SourceOrderId != sourceOrderId || operation.AmendmentId != header.AmendmentId
                || operation.State != OrderAmendmentResolutionOperationState.Resolved
                || !links.TryGetValue(header.OwnerLinkId, out var owner)
                || owner.OrderId != sourceOrderId
                || !TryReadRetainedOriginal(header, owner, evidence.Snapshot,
                    originalRows, sourceOrderId, out var original))
                throw Held("A prior loyalty compensation is not bound to one resolved source history.");

            var expectedType = header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                ? TransactionType.Earned : TransactionType.Redeemed;
            if (header.Kind is not (OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                    or OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration)
                || original.OrderId != sourceOrderId || original.TransactionType != expectedType
                || original.Points != header.OriginalTransactionPoints
                || header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                    && original.Points <= 0
                || header.Kind == OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration
                    && original.Points >= 0)
                throw Held("A prior compensation does not match its exact original ledger transaction.");
            if (header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                ? header.AwardWitnessId != evidence.AwardWitness?.Id
                    || owner.Slot != OrderBillingSnapshotOwnerSlot.Earning
                : header.AwardWitnessId.HasValue || owner.Slot != OrderBillingSnapshotOwnerSlot.Redemption)
                throw Held("A prior compensation uses the wrong award or redemption owner authority.");

            var rows = evidence.PriorUnits.Where(value => value.CompensationId == header.Id).ToArray();
            if (rows.Length == 0 || rows.Any(value => value.SourceOrderId != sourceOrderId
                    || value.Kind != header.Kind || value.Points <= 0
                    || !unitsById.TryGetValue(value.SnapshotUnitId, out var unit)
                    || value.Points != (header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                        ? coverageByUnit.GetValueOrDefault(unit.Id)?.EligibleEarnedPoints : unit.RedeemedPoints)
                    || !usedUnitKinds.Add((value.SnapshotUnitId, value.Kind)))
                || rows.Sum(value => (long)value.Points) != header.RequiredPoints)
                throw Held("A prior compensation does not conserve exact frozen unit attribution.");
            if (header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback)
                foreach (var row in rows) compensatedEarnedUnits.Add(row.SnapshotUnitId);

            var posting = evidence.PriorPostings.Where(value => value.CompensationId == header.Id).ToArray();
            var reservation = evidence.Reservations.Where(value => value.CompensationId == header.Id).ToArray();
            if (posting.Length != 1 || posting[0].MovementTransactionId == Guid.Empty
                || posting[0].PointsDelta != (header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                    ? -header.RequiredPoints : header.RequiredPoints)
                || header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                    && (reservation.Length != 1 || reservation[0].SourceOrderId != sourceOrderId
                        || reservation[0].OperationId != header.OperationId
                        || reservation[0].OwnerLinkId != header.OwnerLinkId
                        || reservation[0].State != OrderAmendmentLoyaltyReservationState.Consumed)
                || header.Kind == OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration
                    && reservation.Length != 0)
                throw Held("A resolved loyalty compensation lacks its exact posting or reservation receipt.");
            if (!originalRows.TryGetValue(posting[0].MovementTransactionId, out var movement)
                || movement.OrderId != sourceOrderId || movement.UserId != original.UserId
                || movement.OriginalTransactionId != header.OriginalTransactionId
                || movement.TransactionType != (header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                    ? TransactionType.EarnedClawback : TransactionType.RedemptionRestored)
                || movement.Points != posting[0].PointsDelta)
                throw Held("A prior compensation posting does not match its source-order ledger movement.");

            var plan = new OrderAmendmentLoyaltyCompensationPlan(header.SnapshotId,
                header.OwnerLinkId, header.OriginalTransactionId, header.AwardWitnessId,
                header.Kind, header.OriginalTransactionPoints, header.RequiredPoints,
                string.Empty, rows.OrderBy(value => value.SnapshotUnitId)
                    .Select(value => new OrderAmendmentLoyaltyCompensationUnitPlan(
                        value.SnapshotUnitId, value.Points)).ToArray());
            if (header.PlanFingerprint != OrderAmendmentLoyaltyPlanFingerprint.CreateCompensation(
                    sourceOrderId, header.AmendmentId, plan))
                throw Held("A prior compensation fingerprint does not match its immutable units.");
        }
        return compensatedEarnedUnits;
    }

    private static bool MatchesRetainedOwner(OrderBillingSnapshotOwnerLink owner,
        FidelityPointsTransaction transaction)
    {
        if (owner.Disposition == OrderBillingSnapshotOwnerDisposition.Linked)
            return owner.UserId.HasValue && !owner.ErasedAt.HasValue && owner.ErasureTransactionId is null
                && transaction.UserId == owner.UserId;
        return MatchesErasedOwner(owner) && transaction.UserId is null;
    }

    private static bool MatchesErasedOwner(OrderBillingSnapshotOwnerLink owner) =>
        owner.Disposition == OrderBillingSnapshotOwnerDisposition.Erased
        && !owner.UserId.HasValue && owner.ErasedAt.HasValue
        && owner.ErasureTransactionId is { Length: >= 1 and <= 20 } erasureId
        && erasureId.All(char.IsAsciiDigit);

    internal static bool TryReadRetainedOriginal(OrderAmendmentLoyaltyCompensation header,
        OrderBillingSnapshotOwnerLink owner, OrderBillingSnapshot? snapshot,
        IReadOnlyDictionary<Guid, FidelityPointsTransaction> originalRows, Guid sourceOrderId,
        out FidelityPointsTransaction original)
    {
        if (originalRows.TryGetValue(header.OriginalTransactionId, out original!))
            return MatchesRetainedOwner(owner, original);

        if (header.Kind != OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration
            || owner.Slot != OrderBillingSnapshotOwnerSlot.Redemption
            || owner.Disposition != OrderBillingSnapshotOwnerDisposition.Erased
            || snapshot is null || snapshot.OrderId != sourceOrderId
            || snapshot.RedemptionTransactionId != header.OriginalTransactionId
            || snapshot.RedemptionTransactionType != TransactionType.Redeemed
            || snapshot.RedemptionTransactionPoints != header.OriginalTransactionPoints
            || snapshot.RedemptionTransactionPoints >= 0
            || -snapshot.RedemptionTransactionPoints != snapshot.RedeemedPoints
            || !snapshot.RedemptionTransactionCreatedAt.HasValue
            || snapshot.RedemptionTransactionCreatedAt.Value == default
            || !MatchesErasedOwner(owner))
        {
            original = null!;
            return false;
        }

        original = new FidelityPointsTransaction
        {
            Id = header.OriginalTransactionId,
            UserId = null,
            OrderId = sourceOrderId,
            TransactionType = TransactionType.Redeemed,
            Points = snapshot.RedemptionTransactionPoints.Value,
            OrderTotal = snapshot.RedemptionTransactionOrderTotal,
            CreatedAt = snapshot.RedemptionTransactionCreatedAt.Value,
            CreatedBy = "RetainedLoyaltyEvidence"
        };
        return true;
    }

    private static void ValidatePriorRemovalCoverage(
        IReadOnlyList<OrderBillingSnapshotUnit> units, OrderAmendmentLoyaltyEvidence evidence,
        IReadOnlySet<Guid> priorRemovedUnitIds, HashSet<Guid> compensatedUnitIds,
        AwardValidation award)
    {
        var unitsById = units.ToDictionary(value => value.Id);
        var suppressed = evidence.Suppressions.Select(value => value.SnapshotUnitId).ToHashSet();
        var awarded = award.Coverage.Select(value => value.SnapshotUnitId).ToHashSet();
        foreach (var unitId in priorRemovedUnitIds)
        {
            if (!unitsById.TryGetValue(unitId, out var unit))
                throw Held("A prior removed loyalty unit is missing from the immutable snapshot.");
            if (unit.EarnedPoints == 0 || suppressed.Contains(unitId))
                continue;
            if (!awarded.Contains(unitId) || !compensatedUnitIds.Contains(unitId))
                throw Held("A previously removed earned unit has neither suppression nor exact clawback proof.");
        }
    }
}
