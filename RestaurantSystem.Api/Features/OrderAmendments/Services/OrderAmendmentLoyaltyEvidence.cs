using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentLoyaltyEvidence(
    OrderBillingSnapshot? Snapshot,
    IReadOnlyList<OrderBillingSnapshotUnit> Units,
    IReadOnlyList<OrderBillingSnapshotOwnerLink> OwnerLinks,
    OrderBillingAwardWitness? AwardWitness,
    IReadOnlyList<OrderBillingAwardUnitCoverage> AwardCoverage,
    IReadOnlyList<OrderBillingUnitAwardSuppression> Suppressions,
    IReadOnlyList<OrderAmendment> CommittedAmendments,
    IReadOnlyList<FidelityPointsTransaction> Transactions,
    IReadOnlyList<OrderAmendmentLoyaltyCompensation> PriorCompensations,
    IReadOnlyList<OrderAmendmentLoyaltyCompensationUnit> PriorUnits,
    IReadOnlyList<OrderAmendmentLoyaltyCompensationPosting> PriorPostings,
    IReadOnlyList<OrderAmendmentLoyaltyReservation> Reservations,
    IReadOnlyList<OrderAmendmentResolutionOperation> Operations,
    OrderBillingEarningRetirement? Retirement)
{
    internal static OrderAmendmentLoyaltyEvidence Empty { get; } = new(
        null, [], [], null, [], [], [], [], [], [], [], [], [], null);

    internal OrderAmendmentLoyaltyEvidence WithoutOperation(Guid operationId)
    {
        var excluded = PriorCompensations.Where(value => value.OperationId == operationId)
            .Select(value => value.Id).ToHashSet();
        return this with
        {
            PriorCompensations = PriorCompensations.Where(value => !excluded.Contains(value.Id)).ToArray(),
            PriorUnits = PriorUnits.Where(value => !excluded.Contains(value.CompensationId)).ToArray(),
            PriorPostings = PriorPostings.Where(value => !excluded.Contains(value.CompensationId)).ToArray(),
            Reservations = Reservations.Where(value => !excluded.Contains(value.CompensationId)).ToArray()
        };
    }
}
