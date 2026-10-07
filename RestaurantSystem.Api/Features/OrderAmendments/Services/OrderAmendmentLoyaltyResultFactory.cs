using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentLoyaltyResultEvidence(
    OrderAmendmentLoyaltyPlan? Plan,
    OrderBillingAwardWitness? AwardWitness,
    IReadOnlyList<OrderBillingSnapshotOwnerLink> OwnerLinks,
    IReadOnlyList<OrderAmendmentLoyaltyCompensation> Compensations,
    IReadOnlyList<OrderAmendmentLoyaltyReservation> Reservations,
    IReadOnlyList<OrderAmendmentLoyaltyCompensationPosting> Postings,
    long? AvailablePointsBeforeClawback);

internal static class OrderAmendmentLoyaltyResultFactory
{
    internal static OrderAmendmentLoyaltyResultDto? Create(
        OrderAmendmentResolutionOperation operation, OrderAmendmentLoyaltyResultEvidence evidence)
    {
        var plan = evidence.Plan;
        if (plan?.SnapshotId is null)
            return null;
        ValidateCompensations(operation, plan, evidence.Compensations);
        var reservation = ValidateReservation(operation, plan, evidence.Compensations, evidence.Reservations);
        ValidatePostings(operation, evidence.Compensations, evidence.Postings);

        var links = evidence.OwnerLinks.ToDictionary(value => value.Id);
        var requiredOwners = new[] { plan.EarningOwnerLinkId, plan.RedemptionOwnerLinkId }
            .Where(value => value.HasValue).Select(value => value!.Value)
            .Concat(evidence.Compensations.Select(value => value.OwnerLinkId)).Distinct().ToArray();
        var ownerAvailable = requiredOwners.All(id => links.TryGetValue(id, out var link)
            && link.OrderId == operation.SourceOrderId
            && link.Disposition == OrderBillingSnapshotOwnerDisposition.Linked
            && link.UserId.HasValue && !link.ErasedAt.HasValue && link.ErasureTransactionId is null);
        var awardPending = plan.AwardPending && evidence.AwardWitness is null;
        var applied = awardPending ? plan.AppliedAwardPoints
            : plan.AwardPending ? evidence.AwardWitness!.AppliedPoints : plan.AppliedAwardPoints;
        var suppressed = awardPending ? plan.SuppressedPoints
            : plan.AwardPending ? evidence.AwardWitness!.SuppressedPoints : plan.SuppressedPoints;
        if (applied < 0 || suppressed < 0 || (long)applied + suppressed > plan.CandidatePoints
            || !awardPending && (long)applied + suppressed != plan.CandidatePoints)
            throw Invalid("The current award result differs from its accepted loyalty candidate.");

        var hasEffect = plan.PendingAwardSuppressions.Count > 0 || plan.Compensations.Count > 0;
        var status = operation.State == OrderAmendmentResolutionOperationState.Resolved
            ? hasEffect ? OrderAmendmentLoyaltyOperationStatus.Resolved
                : OrderAmendmentLoyaltyOperationStatus.None
            : ReadStatus(plan, reservation, ownerAvailable, awardPending, hasEffect);
        var activeReservation = reservation?.State is OrderAmendmentLoyaltyReservationState.HeldShortfall
            or OrderAmendmentLoyaltyReservationState.Reserved;
        int? shortfall = activeReservation && evidence.AvailablePointsBeforeClawback is long available
            ? checked((int)Math.Max(0L, plan.EarnedClawbackPoints - available)) : null;

        return new OrderAmendmentLoyaltyResultDto(status, awardPending, plan.CandidatePoints,
            applied, suppressed, plan.EarnedClawbackPoints, plan.RedemptionRestorationPoints,
            SumPosted(evidence.Compensations, evidence.Postings,
                OrderAmendmentLoyaltyCompensationKind.EarnedClawback),
            SumPosted(evidence.Compensations, evidence.Postings,
                OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration),
            evidence.AvailablePointsBeforeClawback, shortfall);
    }

    private static OrderAmendmentLoyaltyOperationStatus ReadStatus(
        OrderAmendmentLoyaltyPlan plan, OrderAmendmentLoyaltyReservation? reservation,
        bool ownerAvailable, bool awardPending, bool hasEffect)
    {
        if (!hasEffect)
            return OrderAmendmentLoyaltyOperationStatus.None;
        if (!ownerAvailable)
            return OrderAmendmentLoyaltyOperationStatus.OwnerUnavailable;
        if (reservation?.State == OrderAmendmentLoyaltyReservationState.HeldShortfall)
            return OrderAmendmentLoyaltyOperationStatus.HeldShortfall;
        if (reservation?.State == OrderAmendmentLoyaltyReservationState.Reserved)
            return OrderAmendmentLoyaltyOperationStatus.Reserved;
        if (reservation?.State == OrderAmendmentLoyaltyReservationState.Released)
            return OrderAmendmentLoyaltyOperationStatus.ReleasedAfterNoRefund;
        if (awardPending && plan.PendingAwardSuppressions.Count > 0)
            return OrderAmendmentLoyaltyOperationStatus.AwaitingAwardSuppression;
        return plan.Compensations.Count > 0
            ? OrderAmendmentLoyaltyOperationStatus.PendingSettlement
            : OrderAmendmentLoyaltyOperationStatus.None;
    }

    private static void ValidateCompensations(OrderAmendmentResolutionOperation operation,
        OrderAmendmentLoyaltyPlan plan, IReadOnlyList<OrderAmendmentLoyaltyCompensation> rows)
    {
        if (rows.Count != plan.Compensations.Count)
            throw Invalid("The saved loyalty compensation headers differ from the reviewed plan.");
        foreach (var expected in plan.Compensations)
        {
            var row = rows.SingleOrDefault(value => value.OriginalTransactionId == expected.OriginalTransactionId
                && value.Kind == expected.Kind);
            if (row is null || row.SourceOrderId != operation.SourceOrderId
                || row.AmendmentId != operation.AmendmentId || row.OperationId != operation.Id
                || row.SnapshotId != expected.SnapshotId || row.OwnerLinkId != expected.OwnerLinkId
                || row.AwardWitnessId != expected.AwardWitnessId
                || row.OriginalTransactionPoints != expected.OriginalTransactionPoints
                || row.RequiredPoints != expected.RequiredPoints || row.PlanFingerprint != expected.PlanFingerprint)
                throw Invalid("A saved loyalty compensation header is not bound to the reviewed operation.");
        }
    }

    private static OrderAmendmentLoyaltyReservation? ValidateReservation(
        OrderAmendmentResolutionOperation operation, OrderAmendmentLoyaltyPlan plan,
        IReadOnlyList<OrderAmendmentLoyaltyCompensation> compensations,
        IReadOnlyList<OrderAmendmentLoyaltyReservation> rows)
    {
        var expected = plan.Compensations.SingleOrDefault(value =>
            value.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback);
        if ((expected is null && rows.Count != 0) || (expected is not null && rows.Count != 1))
            throw Invalid("The exact earned-points obligation reservation is missing or duplicated.");
        if (expected is null)
            return null;
        var row = rows.Single();
        var header = expected;
        var compensation = compensations.Single(value => value.Kind == expected.Kind
            && value.OriginalTransactionId == expected.OriginalTransactionId);
        if (row.SourceOrderId != operation.SourceOrderId || row.OperationId != operation.Id
            || row.CompensationId != compensation.Id || row.OwnerLinkId != header.OwnerLinkId)
            throw Invalid("The loyalty reservation is not bound to its exact clawback.");
        return row;
    }

    private static void ValidatePostings(OrderAmendmentResolutionOperation operation,
        IReadOnlyList<OrderAmendmentLoyaltyCompensation> headers,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationPosting> postings)
    {
        if (operation.State == OrderAmendmentResolutionOperationState.Resolved
                ? postings.Count != headers.Count
                : postings.Count != 0)
            throw Invalid("The loyalty posting receipts do not match the settlement state.");
        var headersById = headers.ToDictionary(value => value.Id);
        if (postings.Any(value => !headersById.TryGetValue(value.CompensationId, out var header)
                || value.PointsDelta != (header.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                    ? -header.RequiredPoints : header.RequiredPoints))
            || postings.Select(value => value.CompensationId).Distinct().Count() != postings.Count)
            throw Invalid("The loyalty posting receipts do not conserve their exact obligations.");
    }

    private static int SumPosted(IReadOnlyList<OrderAmendmentLoyaltyCompensation> headers,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationPosting> postings,
        OrderAmendmentLoyaltyCompensationKind kind)
    {
        var ids = headers.Where(value => value.Kind == kind).Select(value => value.Id).ToHashSet();
        return checked((int)postings.Where(value => ids.Contains(value.CompensationId))
            .Sum(value => Math.Abs((long)value.PointsDelta)));
    }

    private static ConflictException Invalid(string message) => new(message);
}
