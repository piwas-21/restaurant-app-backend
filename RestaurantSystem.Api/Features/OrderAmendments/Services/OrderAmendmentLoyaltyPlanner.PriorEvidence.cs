using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentLoyaltyPlanner
{
    private const string PriorCompensationSourceHistoryMessage =
        "A prior loyalty compensation is not bound to one resolved source history.";

    private sealed record PriorEvidenceIndexes(
        IReadOnlyDictionary<Guid, OrderBillingSnapshotUnit> UnitsById,
        IReadOnlyDictionary<Guid, OrderAmendment> Amendments,
        IReadOnlyDictionary<Guid, OrderAmendmentResolutionOperation> Operations,
        IReadOnlyDictionary<Guid, FidelityPointsTransaction> OriginalRows,
        IReadOnlyDictionary<Guid, OrderBillingAwardUnitCoverage> CoverageByUnit,
        IReadOnlyDictionary<Guid, OrderBillingSnapshotOwnerLink> OwnerLinks,
        IReadOnlySet<Guid> CompensationIds);

    private sealed record PriorHeaderContext(
        OrderBillingSnapshotOwnerLink Owner, FidelityPointsTransaction Original);

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

        var indexes = BuildPriorEvidenceIndexes(units, evidence);
        EnsurePriorDetailsHaveHeaders(indexes.CompensationIds, evidence);
        var compensatedEarnedUnits = new HashSet<Guid>();
        var usedUnitKinds = new HashSet<(Guid UnitId, OrderAmendmentLoyaltyCompensationKind Kind)>();
        foreach (var header in evidence.PriorCompensations)
        {
            var prior = ValidatePriorHeader(header, sourceOrderId, snapshotId, evidence, indexes);
            ValidateOriginalTransaction(header, prior.Owner, prior.Original, evidence);
            var rows = ValidatePriorUnits(header, sourceOrderId, evidence.PriorUnits,
                indexes, usedUnitKinds);
            if (header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback)
                foreach (var row in rows)
                    compensatedEarnedUnits.Add(row.SnapshotUnitId);

            var posting = ValidatePriorReceipt(header, sourceOrderId, evidence);
            ValidatePriorMovement(header, sourceOrderId, prior.Original, posting, indexes.OriginalRows);
            ValidatePriorFingerprint(header, sourceOrderId, rows);
        }
        return compensatedEarnedUnits;
    }

    private static PriorEvidenceIndexes BuildPriorEvidenceIndexes(
        IReadOnlyList<OrderBillingSnapshotUnit> units, OrderAmendmentLoyaltyEvidence evidence) => new(
        units.ToDictionary(value => value.Id),
        evidence.CommittedAmendments.ToDictionary(value => value.Id),
        evidence.Operations.ToDictionary(value => value.Id),
        evidence.Transactions.ToDictionary(value => value.Id),
        evidence.AwardCoverage.ToDictionary(value => value.SnapshotUnitId),
        evidence.OwnerLinks.ToDictionary(value => value.Id),
        evidence.PriorCompensations.Select(value => value.Id).ToHashSet());

    private static void EnsurePriorDetailsHaveHeaders(
        IReadOnlySet<Guid> compensationIds, OrderAmendmentLoyaltyEvidence evidence)
    {
        if (evidence.PriorUnits.Any(value => !compensationIds.Contains(value.CompensationId))
            || evidence.PriorPostings.Any(value => !compensationIds.Contains(value.CompensationId))
            || evidence.Reservations.Any(value => !compensationIds.Contains(value.CompensationId)))
            throw Held("Loyalty compensation details reference an unknown header.");
    }

    private static PriorHeaderContext ValidatePriorHeader(
        OrderAmendmentLoyaltyCompensation header, Guid sourceOrderId, Guid snapshotId,
        OrderAmendmentLoyaltyEvidence evidence, PriorEvidenceIndexes indexes)
    {
        ValidatePriorHeaderIdentity(header, sourceOrderId, snapshotId);
        if (!indexes.Amendments.TryGetValue(header.AmendmentId, out var amendment)
            || amendment.State != OrderAmendmentState.Committed
            || !indexes.Operations.TryGetValue(header.OperationId, out var operation)
            || operation.SourceOrderId != sourceOrderId || operation.AmendmentId != header.AmendmentId
            || operation.State != OrderAmendmentResolutionOperationState.Resolved)
            throw Held(PriorCompensationSourceHistoryMessage);
        if (!indexes.OwnerLinks.TryGetValue(header.OwnerLinkId, out var owner)
            || owner.OrderId != sourceOrderId)
            throw Held(PriorCompensationSourceHistoryMessage);
        if (!TryReadRetainedOriginal(header, owner, evidence.Snapshot,
                indexes.OriginalRows, sourceOrderId, out var original))
            throw Held(PriorCompensationSourceHistoryMessage);
        return new(owner, original);
    }

    private static void ValidatePriorHeaderIdentity(
        OrderAmendmentLoyaltyCompensation header, Guid sourceOrderId, Guid snapshotId)
    {
        if (header.SourceOrderId != sourceOrderId || header.SnapshotId != snapshotId
            || header.Id == Guid.Empty || header.AmendmentId == Guid.Empty
            || header.OperationId == Guid.Empty || header.RequiredPoints <= 0)
            throw Held(PriorCompensationSourceHistoryMessage);
    }

    private static void ValidateOriginalTransaction(
        OrderAmendmentLoyaltyCompensation header, OrderBillingSnapshotOwnerLink owner,
        FidelityPointsTransaction original, OrderAmendmentLoyaltyEvidence evidence)
    {
        var expectedType = header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
            ? TransactionType.Earned : TransactionType.Redeemed;
        if (header.Kind is not (OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                or OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration)
            || original.OrderId != header.SourceOrderId || original.TransactionType != expectedType
            || original.Points != header.OriginalTransactionPoints
            || header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback && original.Points <= 0
            || header.Kind == OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration && original.Points >= 0)
            throw Held("A prior compensation does not match its exact original ledger transaction.");
        ValidatePriorOwnerAuthority(header, owner, evidence.AwardWitness);
    }

    private static void ValidatePriorOwnerAuthority(
        OrderAmendmentLoyaltyCompensation header, OrderBillingSnapshotOwnerLink owner,
        OrderBillingAwardWitness? awardWitness)
    {
        var validAuthority = header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
            ? header.AwardWitnessId == awardWitness?.Id && owner.Slot == OrderBillingSnapshotOwnerSlot.Earning
            : !header.AwardWitnessId.HasValue && owner.Slot == OrderBillingSnapshotOwnerSlot.Redemption;
        if (!validAuthority)
            throw Held("A prior compensation uses the wrong award or redemption owner authority.");
    }

    private static OrderAmendmentLoyaltyCompensationUnit[] ValidatePriorUnits(
        OrderAmendmentLoyaltyCompensation header, Guid sourceOrderId,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationUnit> allUnits,
        PriorEvidenceIndexes indexes,
        HashSet<(Guid UnitId, OrderAmendmentLoyaltyCompensationKind Kind)> usedUnitKinds)
    {
        var rows = allUnits.Where(value => value.CompensationId == header.Id).ToArray();
        if (rows.Length == 0)
            throw Held("A prior compensation does not conserve exact frozen unit attribution.");
        foreach (var row in rows)
            ValidatePriorUnit(row, header, sourceOrderId, indexes, usedUnitKinds);
        if (rows.Sum(value => (long)value.Points) != header.RequiredPoints)
            throw Held("A prior compensation does not conserve exact frozen unit attribution.");
        return rows;
    }

    private static void ValidatePriorUnit(
        OrderAmendmentLoyaltyCompensationUnit row, OrderAmendmentLoyaltyCompensation header,
        Guid sourceOrderId, PriorEvidenceIndexes indexes,
        HashSet<(Guid UnitId, OrderAmendmentLoyaltyCompensationKind Kind)> usedUnitKinds)
    {
        if (row.SourceOrderId != sourceOrderId || row.Kind != header.Kind || row.Points <= 0
            || !indexes.UnitsById.TryGetValue(row.SnapshotUnitId, out var unit)
            || row.Points != ReadUnitPoints(header.Kind, unit, indexes.CoverageByUnit)
            || !usedUnitKinds.Add((row.SnapshotUnitId, row.Kind)))
            throw Held("A prior compensation does not conserve exact frozen unit attribution.");
    }

    private static int? ReadUnitPoints(
        OrderAmendmentLoyaltyCompensationKind kind, OrderBillingSnapshotUnit unit,
        IReadOnlyDictionary<Guid, OrderBillingAwardUnitCoverage> coverageByUnit) =>
        kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
            ? coverageByUnit.GetValueOrDefault(unit.Id)?.EligibleEarnedPoints
            : unit.RedeemedPoints;

    private static OrderAmendmentLoyaltyCompensationPosting ValidatePriorReceipt(
        OrderAmendmentLoyaltyCompensation header, Guid sourceOrderId,
        OrderAmendmentLoyaltyEvidence evidence)
    {
        var postings = evidence.PriorPostings.Where(value => value.CompensationId == header.Id).ToArray();
        var reservations = evidence.Reservations.Where(value => value.CompensationId == header.Id).ToArray();
        if (postings.Length != 1 || postings[0].MovementTransactionId == Guid.Empty
            || postings[0].PointsDelta != ReadExpectedDelta(header))
            throw Held("A resolved loyalty compensation lacks its exact posting or reservation receipt.");
        ValidatePriorReservation(header, sourceOrderId, reservations);
        return postings[0];
    }

    private static int ReadExpectedDelta(OrderAmendmentLoyaltyCompensation header) =>
        header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
            ? -header.RequiredPoints : header.RequiredPoints;

    private static void ValidatePriorReservation(
        OrderAmendmentLoyaltyCompensation header, Guid sourceOrderId,
        OrderAmendmentLoyaltyReservation[] reservations)
    {
        if (header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback)
        {
            if (reservations.Length != 1 || reservations[0].SourceOrderId != sourceOrderId
                || reservations[0].OperationId != header.OperationId
                || reservations[0].OwnerLinkId != header.OwnerLinkId
                || reservations[0].State != OrderAmendmentLoyaltyReservationState.Consumed)
                throw Held("A resolved loyalty compensation lacks its exact posting or reservation receipt.");
            return;
        }
        if (reservations.Length != 0)
            throw Held("A resolved loyalty compensation lacks its exact posting or reservation receipt.");
    }

    private static void ValidatePriorMovement(
        OrderAmendmentLoyaltyCompensation header, Guid sourceOrderId,
        FidelityPointsTransaction original, OrderAmendmentLoyaltyCompensationPosting posting,
        IReadOnlyDictionary<Guid, FidelityPointsTransaction> originalRows)
    {
        if (!originalRows.TryGetValue(posting.MovementTransactionId, out var movement)
            || movement.OrderId != sourceOrderId || movement.UserId != original.UserId
            || movement.OriginalTransactionId != header.OriginalTransactionId
            || movement.TransactionType != ReadMovementType(header.Kind)
            || movement.Points != posting.PointsDelta)
            throw Held("A prior compensation posting does not match its source-order ledger movement.");
    }

    private static TransactionType ReadMovementType(OrderAmendmentLoyaltyCompensationKind kind) =>
        kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
            ? TransactionType.EarnedClawback : TransactionType.RedemptionRestored;

    private static void ValidatePriorFingerprint(
        OrderAmendmentLoyaltyCompensation header, Guid sourceOrderId,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationUnit> rows)
    {
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
