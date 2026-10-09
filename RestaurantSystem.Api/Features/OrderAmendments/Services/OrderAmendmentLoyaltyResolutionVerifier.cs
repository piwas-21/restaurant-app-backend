using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentLoyaltyResolutionVerifier
{
    internal static void AssertPlanTransition(Guid sourceOrderId, Guid amendmentId,
        OrderAmendmentLoyaltyPlan? reviewed, OrderAmendmentLoyaltyPlan current,
        OrderAmendmentLoyaltyEvidence evidence)
    {
        if (reviewed is null)
        {
            AssertNoNewSnapshot(current);
            return;
        }
        AssertReviewedIdentity(reviewed, current);
        if (!reviewed.AwardPending)
        {
            AssertFinalAwardTransition(sourceOrderId, amendmentId, reviewed, current);
            return;
        }
        AssertPendingAwardTransition(sourceOrderId, amendmentId, reviewed, current, evidence);
    }

    private static void AssertNoNewSnapshot(OrderAmendmentLoyaltyPlan current)
    {
        if (current.SnapshotId.HasValue)
            throw Invalid();
    }

    private static void AssertReviewedIdentity(
        OrderAmendmentLoyaltyPlan reviewed, OrderAmendmentLoyaltyPlan current)
    {
        if (!reviewed.SnapshotId.HasValue || current.SnapshotId != reviewed.SnapshotId
            || current.Currency != reviewed.Currency || current.CandidatePoints != reviewed.CandidatePoints
            || current.EarningOwnerLinkId != reviewed.EarningOwnerLinkId
            || current.RedemptionOwnerLinkId != reviewed.RedemptionOwnerLinkId
            || !reviewed.RemovedUnits.SequenceEqual(current.RemovedUnits)
            || !CompensationsMatch(reviewed.Compensations, current.Compensations))
            throw Invalid();
    }

    private static void AssertFinalAwardTransition(
        Guid sourceOrderId, Guid amendmentId,
        OrderAmendmentLoyaltyPlan reviewed, OrderAmendmentLoyaltyPlan current)
    {
        if (current.AwardPending || current.AppliedAwardPoints != reviewed.AppliedAwardPoints
            || current.SuppressedPoints != reviewed.SuppressedPoints
            || current.EarnedClawbackPoints != reviewed.EarnedClawbackPoints
            || current.RedemptionRestorationPoints != reviewed.RedemptionRestorationPoints)
            throw Invalid();
        var before = OrderAmendmentLoyaltyPlanFingerprint.Create(sourceOrderId, amendmentId, reviewed);
        var now = OrderAmendmentLoyaltyPlanFingerprint.Create(sourceOrderId, amendmentId, current);
        if (before != now)
            throw Invalid();
    }

    private static void AssertPendingAwardTransition(
        Guid sourceOrderId, Guid amendmentId, OrderAmendmentLoyaltyPlan reviewed,
        OrderAmendmentLoyaltyPlan current, OrderAmendmentLoyaltyEvidence evidence)
    {
        ValidatePendingSuppressionRows(sourceOrderId, amendmentId, reviewed, current, evidence);
        if (current.AwardPending)
        {
            ValidateStillPending(reviewed, current);
            return;
        }
        ValidateAwardSettledAfterPending(reviewed, current, evidence, amendmentId);
    }

    private static void ValidatePendingSuppressionRows(
        Guid sourceOrderId, Guid amendmentId, OrderAmendmentLoyaltyPlan reviewed,
        OrderAmendmentLoyaltyPlan current, OrderAmendmentLoyaltyEvidence evidence)
    {
        var expectedSuppressionIds = reviewed.RemovedUnits.Where(value => value.EarnedPoints > 0)
            .Select(value => value.SnapshotUnitId).Order().ToArray();
        var actualSuppressionIds = evidence.Suppressions.Where(value => value.AmendmentId == amendmentId
                && value.OrderId == sourceOrderId)
            .Select(value => value.SnapshotUnitId).Order().ToArray();
        if (!expectedSuppressionIds.SequenceEqual(actualSuppressionIds)
            || current.PendingAwardSuppressions.Count != 0)
            throw Invalid();
    }

    private static void ValidateStillPending(
        OrderAmendmentLoyaltyPlan reviewed, OrderAmendmentLoyaltyPlan current)
    {
        if (current.AppliedAwardPoints != 0 || current.SuppressedPoints != reviewed.SuppressedPoints
            || current.EarnedClawbackPoints != 0)
            throw Invalid();
    }

    private static void ValidateAwardSettledAfterPending(
        OrderAmendmentLoyaltyPlan reviewed, OrderAmendmentLoyaltyPlan current,
        OrderAmendmentLoyaltyEvidence evidence, Guid amendmentId)
    {
        if (current.EarnedClawbackPoints != 0
            || current.SuppressedPoints != reviewed.SuppressedPoints
            || current.RedemptionRestorationPoints != reviewed.RedemptionRestorationPoints
            || evidence.AwardWitness?.SuppressedPoints
                != checked(SumSuppressedBeforeCurrent(evidence, amendmentId)
                    + reviewed.RemovedUnits.Sum(value => (long)value.EarnedPoints)))
            throw Invalid();
        var removedIds = reviewed.RemovedUnits.Select(value => value.SnapshotUnitId).ToHashSet();
        if (evidence.AwardCoverage.Any(value => removedIds.Contains(value.SnapshotUnitId)))
            throw Invalid();
    }

    private static bool CompensationsMatch(
        IReadOnlyList<OrderAmendmentLoyaltyCompensationPlan> left,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationPlan> right) =>
        left.Count == right.Count && left.OrderBy(value => value.Kind)
            .ThenBy(value => value.OriginalTransactionId).Select(value => value.PlanFingerprint)
            .SequenceEqual(right.OrderBy(value => value.Kind)
                .ThenBy(value => value.OriginalTransactionId).Select(value => value.PlanFingerprint));

    private static int SumSuppressedBeforeCurrent(
        OrderAmendmentLoyaltyEvidence evidence, Guid currentAmendmentId) =>
        checked((int)evidence.Suppressions.Where(value => value.AmendmentId != currentAmendmentId)
            .Sum(value => (long)value.SuppressedEarnedPoints));

    private static ConflictException Invalid() => new(
        "The loyalty compensation evidence changed after its reviewed plan; keep the settlement held.");
}
