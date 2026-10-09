using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentLoyaltyEvidenceFingerprint
{
    internal static string Create(OrderAmendmentLoyaltyEvidence evidence,
        Guid? excludeOperationId = null, Guid? excludeAmendmentId = null,
        bool allowAwardAfterPendingSuppression = false)
    {
        var compensations = evidence.PriorCompensations
            .Where(value => value.OperationId != excludeOperationId).ToArray();
        var compensationIds = compensations.Select(value => value.Id).ToHashSet();
        var snapshot = new EvidenceSnapshot(
            evidence.Snapshot is null ? null : HeaderSnapshot.From(evidence.Snapshot),
            evidence.Units.OrderBy(value => value.OrderItemId).ThenBy(value => value.UnitOrdinal)
                .Select(UnitSnapshot.From).ToArray(),
            evidence.OwnerLinks.OrderBy(value => value.Slot).Select(OwnerSnapshot.From).ToArray(),
            !allowAwardAfterPendingSuppression && evidence.AwardWitness is not null
                ? AwardSnapshot.From(evidence.AwardWitness) : null,
            evidence.AwardCoverage.Where(_ => !allowAwardAfterPendingSuppression)
                .OrderBy(value => value.SnapshotUnitId)
                .Select(CoverageSnapshot.From).ToArray(),
            evidence.Suppressions.Where(value => value.AmendmentId != excludeAmendmentId)
                .OrderBy(value => value.SnapshotUnitId)
                .Select(SuppressionSnapshot.From).ToArray(),
            evidence.Transactions.Where(value => !allowAwardAfterPendingSuppression
                    || value.TransactionType != TransactionType.Earned)
                .OrderBy(value => value.Id)
                .Select(TransactionSnapshot.From).ToArray(),
            compensations.OrderBy(value => value.Id)
                .Select(CompensationSnapshot.From).ToArray(),
            evidence.PriorUnits.Where(value => compensationIds.Contains(value.CompensationId))
                .OrderBy(value => value.CompensationId).ThenBy(value => value.SnapshotUnitId)
                .Select(CompensationUnitSnapshot.From).ToArray(),
            evidence.PriorPostings.Where(value => compensationIds.Contains(value.CompensationId))
                .OrderBy(value => value.CompensationId)
                .Select(PostingSnapshot.From).ToArray(),
            evidence.Reservations.Where(value => compensationIds.Contains(value.CompensationId))
                .OrderBy(value => value.CompensationId)
                .Select(ReservationSnapshot.From).ToArray(),
            evidence.Operations.Where(value => value.Id != excludeOperationId).OrderBy(value => value.Id)
                .Select(OperationSnapshot.From).ToArray());
        var legacyHash = OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(snapshot));
        if (evidence.Snapshot?.EarningDisposition is null && evidence.Retirement is null)
            return legacyHash;

        var disposition = evidence.Snapshot?.EffectiveEarningDisposition
            ?? OrderBillingEarningDisposition.Unevaluated;
        var retirement = evidence.Retirement is null ? null : new RetirementSnapshot(
            evidence.Retirement.Id, evidence.Retirement.OrderId, evidence.Retirement.SnapshotId,
            evidence.Retirement.AmendmentId, evidence.Retirement.RetiredUnitCount,
            evidence.Retirement.CreatedAt);
        return OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(
            new ExtendedEvidenceSnapshot(legacyHash, disposition, retirement)));
    }

    private sealed record ExtendedEvidenceSnapshot(
        string ExistingEvidenceHash, OrderBillingEarningDisposition EarningDisposition,
        RetirementSnapshot? Retirement);

    private sealed record RetirementSnapshot(
        Guid Id, Guid OrderId, Guid SnapshotId, Guid AmendmentId, int RetiredUnitCount, DateTime CreatedAt);

    private sealed record EvidenceSnapshot(
        HeaderSnapshot? Snapshot, IReadOnlyList<UnitSnapshot> Units,
        IReadOnlyList<OwnerSnapshot> OwnerLinks, AwardSnapshot? Award,
        IReadOnlyList<CoverageSnapshot> Coverage, IReadOnlyList<SuppressionSnapshot> Suppressions,
        IReadOnlyList<TransactionSnapshot> Transactions,
        IReadOnlyList<CompensationSnapshot> Compensations,
        IReadOnlyList<CompensationUnitSnapshot> CompensationUnits,
        IReadOnlyList<PostingSnapshot> Postings, IReadOnlyList<ReservationSnapshot> Reservations,
        IReadOnlyList<OperationSnapshot> Operations);

    private sealed record HeaderSnapshot(
        Guid Id, Guid OrderId, string Currency, long TaxMinor, int RedeemedPoints,
        long RedemptionDiscountMinor, long EarningBasisMinor, int? Candidate,
        string? EvaluationVersion, string? RuleFingerprint, Guid? RuleId, string? RuleName,
        long? RuleMinimumMinor, long? RuleMaximumMinor, int? RulePoints, int? RulePriority,
        Guid? RedemptionTransactionId, TransactionType? RedemptionType, int? RedemptionPoints,
        decimal? RedemptionOrderTotal, DateTime? RedemptionCreatedAt)
    {
        internal static HeaderSnapshot From(OrderBillingSnapshot value) => new(
            value.Id, value.OrderId, value.Currency, value.TaxMinor, value.RedeemedPoints,
            value.RedemptionDiscountMinor, value.EarningBasisMinor, value.EarnedPointsCandidate,
            value.EarningEvaluationVersion, value.EarningRuleSetFingerprint, value.EarningRuleId,
            value.EarningRuleName, value.EarningRuleMinimumMinor, value.EarningRuleMaximumMinor,
            value.EarningRulePoints, value.EarningRulePriority, value.RedemptionTransactionId,
            value.RedemptionTransactionType, value.RedemptionTransactionPoints,
            value.RedemptionTransactionOrderTotal, value.RedemptionTransactionCreatedAt);
    }

    private sealed record UnitSnapshot(Guid Id, Guid OrderId, Guid OrderItemId,
        int Ordinal, int Earned, int Redeemed, long TaxMinor)
    {
        internal static UnitSnapshot From(OrderBillingSnapshotUnit value) => new(
            value.Id, value.OrderId, value.OrderItemId, value.UnitOrdinal,
            value.EarnedPoints, value.RedeemedPoints, value.TaxMinor);
    }

    private sealed record OwnerSnapshot(Guid Id, Guid OrderId, OrderBillingSnapshotOwnerSlot Slot,
        Guid? UserId, OrderBillingSnapshotOwnerDisposition Disposition,
        DateTime? ErasedAt, string? ErasureTransactionId)
    {
        internal static OwnerSnapshot From(OrderBillingSnapshotOwnerLink value) => new(
            value.Id, value.OrderId, value.Slot, value.UserId, value.Disposition,
            value.ErasedAt, value.ErasureTransactionId);
    }

    private sealed record AwardSnapshot(Guid Id, Guid OrderId, Guid OwnerLinkId,
        OrderBillingAwardOutcome Outcome, int Candidate, int Applied, int Suppressed, Guid? EarnedId)
    {
        internal static AwardSnapshot From(OrderBillingAwardWitness value) => new(
            value.Id, value.OrderId, value.OwnerLinkId, value.Outcome, value.CandidatePoints,
            value.AppliedPoints, value.SuppressedPoints, value.EarnedTransactionId);
    }

    private sealed record CoverageSnapshot(Guid Id, Guid OrderId, Guid AwardId, Guid UnitId, int Points)
    {
        internal static CoverageSnapshot From(OrderBillingAwardUnitCoverage value) => new(
            value.Id, value.OrderId, value.AwardWitnessId, value.SnapshotUnitId, value.EligibleEarnedPoints);
    }

    private sealed record SuppressionSnapshot(Guid Id, Guid OrderId, Guid UnitId, Guid AmendmentId, int Points)
    {
        internal static SuppressionSnapshot From(OrderBillingUnitAwardSuppression value) => new(
            value.Id, value.OrderId, value.SnapshotUnitId, value.AmendmentId,
            value.SuppressedEarnedPoints);
    }

    private sealed record TransactionSnapshot(Guid Id, Guid? UserId, Guid? OrderId,
        TransactionType Type, int Points, Guid? OriginalId, decimal? OrderTotal, DateTime CreatedAt)
    {
        internal static TransactionSnapshot From(FidelityPointsTransaction value) => new(
            value.Id, value.UserId, value.OrderId, value.TransactionType, value.Points,
            value.OriginalTransactionId, value.OrderTotal, value.CreatedAt);
    }

    private sealed record CompensationSnapshot(Guid Id, Guid SourceOrderId, Guid AmendmentId,
        Guid SnapshotId, Guid OwnerLinkId, Guid OriginalId, Guid? AwardId, Guid OperationId,
        OrderAmendmentLoyaltyCompensationKind Kind, int OriginalPoints, int RequiredPoints,
        string Fingerprint)
    {
        internal static CompensationSnapshot From(OrderAmendmentLoyaltyCompensation value) => new(
            value.Id, value.SourceOrderId, value.AmendmentId, value.SnapshotId, value.OwnerLinkId,
            value.OriginalTransactionId, value.AwardWitnessId, value.OperationId, value.Kind,
            value.OriginalTransactionPoints, value.RequiredPoints, value.PlanFingerprint);
    }

    private sealed record CompensationUnitSnapshot(Guid Id, Guid CompensationId, Guid SourceOrderId,
        Guid SnapshotUnitId, OrderAmendmentLoyaltyCompensationKind Kind, int Points)
    {
        internal static CompensationUnitSnapshot From(OrderAmendmentLoyaltyCompensationUnit value) => new(
            value.Id, value.CompensationId, value.SourceOrderId, value.SnapshotUnitId, value.Kind, value.Points);
    }

    private sealed record PostingSnapshot(Guid Id, Guid CompensationId, Guid MovementId,
        int Delta, DateTime PostedAt)
    {
        internal static PostingSnapshot From(OrderAmendmentLoyaltyCompensationPosting value) => new(
            value.Id, value.CompensationId, value.MovementTransactionId, value.PointsDelta, value.PostedAt);
    }

    private sealed record ReservationSnapshot(Guid Id, Guid SourceOrderId, Guid OperationId,
        Guid CompensationId, Guid OwnerLinkId, OrderAmendmentLoyaltyReservationState State)
    {
        internal static ReservationSnapshot From(OrderAmendmentLoyaltyReservation value) => new(
            value.Id, value.SourceOrderId, value.OperationId, value.CompensationId,
            value.OwnerLinkId, value.State);
    }

    private sealed record OperationSnapshot(Guid Id, Guid AmendmentId, Guid SourceOrderId,
        string Currency, long CreditMinor, long RefundMinor, long WaivedMinor,
        string RequestHash, string SnapshotHash, string? ResultHash,
        OrderAmendmentResolutionOperationState State)
    {
        internal static OperationSnapshot From(OrderAmendmentResolutionOperation value) => new(
            value.Id, value.AmendmentId, value.SourceOrderId, value.Currency, value.CreditMinor,
            value.RefundMinor, value.UnpaidWaivedMinor, value.RequestHash,
            OrderAmendmentJson.Hash(value.SnapshotJson),
            value.ResultJson is null ? null : OrderAmendmentJson.Hash(value.ResultJson), value.State);
    }
}
