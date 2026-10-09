namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentLoyaltyPlanFingerprint
{
    internal const string Version = "typed-loyalty-compensation-v1";

    internal static string Create(Guid sourceOrderId, Guid amendmentId, OrderAmendmentLoyaltyPlan plan) =>
        OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(new PlanSnapshot(
            sourceOrderId, amendmentId, plan.Currency, plan.SnapshotId, plan.CandidatePoints,
            plan.AppliedAwardPoints, plan.AwardPending, plan.EarnedClawbackPoints,
            plan.RedemptionRestorationPoints,
            plan.RemovedUnits.OrderBy(value => value.OrderItemId).ThenBy(value => value.UnitOrdinal).ToArray(),
            plan.Compensations.OrderBy(value => value.Kind).ThenBy(value => value.OriginalTransactionId)
                .Select(CompensationSnapshot.From).ToArray())));

    internal static string CreateCompensation(Guid sourceOrderId, Guid amendmentId,
        OrderAmendmentLoyaltyCompensationPlan plan) =>
        OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(new CompensationPlanSnapshot(
            sourceOrderId, amendmentId, plan.SnapshotId, plan.OwnerLinkId,
            plan.OriginalTransactionId, plan.AwardWitnessId, plan.Kind.ToString(),
            plan.OriginalTransactionPoints, plan.RequiredPoints,
            plan.Units.OrderBy(value => value.SnapshotUnitId).ToArray())));

    private sealed record PlanSnapshot(
        Guid SourceOrderId,
        Guid AmendmentId,
        string Currency,
        Guid? SnapshotId,
        int CandidatePoints,
        int AppliedAwardPoints,
        bool AwardPending,
        int EarnedClawbackPoints,
        int RedemptionRestorationPoints,
        IReadOnlyList<OrderAmendmentLoyaltyUnitAllocation> RemovedUnits,
        IReadOnlyList<CompensationSnapshot> Compensations);

    private sealed record CompensationPlanSnapshot(
        Guid SourceOrderId,
        Guid AmendmentId,
        Guid SnapshotId,
        Guid OwnerLinkId,
        Guid OriginalTransactionId,
        Guid? AwardWitnessId,
        string Kind,
        int OriginalTransactionPoints,
        int RequiredPoints,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationUnitPlan> Units);

    private sealed record CompensationSnapshot(
        Guid SnapshotId,
        Guid OwnerLinkId,
        Guid OriginalTransactionId,
        Guid? AwardWitnessId,
        string Kind,
        int OriginalTransactionPoints,
        int RequiredPoints,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationUnitPlan> Units)
    {
        internal static CompensationSnapshot From(OrderAmendmentLoyaltyCompensationPlan value) => new(
            value.SnapshotId, value.OwnerLinkId, value.OriginalTransactionId,
            value.AwardWitnessId, value.Kind.ToString(), value.OriginalTransactionPoints,
            value.RequiredPoints, value.Units.OrderBy(unit => unit.SnapshotUnitId).ToArray());
    }
}
