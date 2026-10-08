using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentLoyaltyPlan(
    string Currency,
    Guid? SnapshotId,
    int CandidatePoints,
    int AppliedAwardPoints,
    bool AwardPending,
    int EarnedClawbackPoints,
    int RedemptionRestorationPoints,
    IReadOnlyList<OrderAmendmentLoyaltyUnitAllocation> RemovedUnits,
    IReadOnlyList<OrderAmendmentLoyaltyCompensationPlan> Compensations,
    IReadOnlyList<OrderAmendmentLoyaltyUnitAllocation> PendingAwardSuppressions,
    Guid? EarningOwnerLinkId = null,
    Guid? RedemptionOwnerLinkId = null,
    int SuppressedPoints = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    OrderBillingEarningDisposition? EarningDisposition = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? EarningRetired = null)
{
    internal static OrderAmendmentLoyaltyPlan Empty(string currency) => new(
        currency, null, 0, 0, false, 0, 0, [], [], []);
}

internal sealed record OrderAmendmentLoyaltyUnitAllocation(
    Guid SnapshotUnitId,
    Guid OrderItemId,
    int UnitOrdinal,
    int EarnedPoints,
    int RedeemedPoints);

internal sealed record OrderAmendmentLoyaltyCompensationPlan(
    Guid SnapshotId,
    Guid OwnerLinkId,
    Guid OriginalTransactionId,
    Guid? AwardWitnessId,
    OrderAmendmentLoyaltyCompensationKind Kind,
    int OriginalTransactionPoints,
    int RequiredPoints,
    string PlanFingerprint,
    IReadOnlyList<OrderAmendmentLoyaltyCompensationUnitPlan> Units);

internal sealed record OrderAmendmentLoyaltyCompensationUnitPlan(
    Guid SnapshotUnitId,
    int Points);
