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
    private sealed record AwardTotals(bool Pending, int Applied, int Suppressed);

    internal static void ValidateSettledJournalEvidence(
        OrderAmendmentResolutionOperation operation,
        OrderAmendmentLoyaltyPlan? plan,
        IReadOnlyList<OrderAmendmentLoyaltyCompensation> compensations,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationUnit> units,
        IReadOnlyList<OrderAmendmentLoyaltyReservation> reservations,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationPosting> postings,
        OrderAmendmentLoyaltyResultDto? result)
    {
        if (operation.State != OrderAmendmentResolutionOperationState.Resolved)
            throw Invalid("Loyalty journal evidence is only valid for a resolved operation.");
        if (plan is null)
        {
            if (compensations.Count != 0 || units.Count != 0 || reservations.Count != 0
                || postings.Count != 0 || result is not null)
                throw Invalid("A loyalty journal exists without an accepted snapshot.");
            return;
        }
        if (plan.SnapshotId is null)
        {
            if (!IsCanonicalEmptyPlan(plan, operation.Currency)
                || compensations.Count != 0 || units.Count != 0 || reservations.Count != 0
                || postings.Count != 0 || result is not null)
                throw Invalid("A loyalty journal exists without an accepted snapshot.");
            return;
        }

        ValidateCompensations(operation, plan, compensations);
        ValidateCompensationUnits(operation, plan, compensations, units);
        var reservation = ValidateReservation(operation, plan, compensations, reservations);
        if (reservation is not null && reservation.State != OrderAmendmentLoyaltyReservationState.Consumed)
            throw Invalid("The settled loyalty clawback reservation was not consumed.");
        ValidatePostings(operation, compensations, postings);
        if (result is null
            || plan.EarnedClawbackPoints != SumRequired(compensations,
                OrderAmendmentLoyaltyCompensationKind.EarnedClawback)
            || plan.RedemptionRestorationPoints != SumRequired(compensations,
                OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration)
            || result.EarnedClawbackPoints != plan.EarnedClawbackPoints
            || result.RedemptionRestorationPoints != plan.RedemptionRestorationPoints
            || result.PostedClawbackPoints != SumPosted(compensations, postings,
                OrderAmendmentLoyaltyCompensationKind.EarnedClawback)
            || result.PostedRestorationPoints != SumPosted(compensations, postings,
                OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration)
            || postings.Any(value => value.MovementTransactionId == Guid.Empty || value.PostedAt == default))
            throw Invalid("The posted loyalty journal differs from its resolved operation result.");
    }

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
        var award = ReadAwardTotals(plan, evidence.AwardWitness);
        var disposition = plan.EarningDisposition ?? OrderBillingEarningDisposition.Evaluated;
        ValidateEarningAward(plan, disposition, award);

        var hasEffect = plan.PendingAwardSuppressions.Count > 0 || plan.Compensations.Count > 0;
        var status = ReadOperationStatus(operation, plan, reservation, ownerAvailable, award.Pending, hasEffect);
        var activeReservation = reservation?.State is OrderAmendmentLoyaltyReservationState.HeldShortfall
            or OrderAmendmentLoyaltyReservationState.Reserved;
        int? shortfall = activeReservation && evidence.AvailablePointsBeforeClawback is long available
            ? checked((int)Math.Max(0L, plan.EarnedClawbackPoints - available)) : null;

        return new OrderAmendmentLoyaltyResultDto(status, award.Pending,
            disposition == OrderBillingEarningDisposition.Evaluated ? plan.CandidatePoints : null,
            award.Applied, award.Suppressed, plan.EarnedClawbackPoints, plan.RedemptionRestorationPoints,
            SumPosted(evidence.Compensations, evidence.Postings,
                OrderAmendmentLoyaltyCompensationKind.EarnedClawback),
            SumPosted(evidence.Compensations, evidence.Postings,
                OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration),
            evidence.AvailablePointsBeforeClawback, shortfall, disposition, plan.EarningRetired == true);
    }

    private static void ValidateEarningAward(
        OrderAmendmentLoyaltyPlan plan, OrderBillingEarningDisposition disposition, AwardTotals award)
    {
        if (disposition == OrderBillingEarningDisposition.Evaluated)
        {
            if (plan.EarningRetired == true || award.Applied < 0 || award.Suppressed < 0
                || (long)award.Applied + award.Suppressed > plan.CandidatePoints
                || !award.Pending && (long)award.Applied + award.Suppressed != plan.CandidatePoints)
                throw Invalid("The current award result differs from its accepted loyalty candidate.");
        }
        else if (disposition is not (OrderBillingEarningDisposition.Unevaluated
                or OrderBillingEarningDisposition.NoCustomerOwnerAtAcceptance
                or OrderBillingEarningDisposition.LoyaltyModuleDisabledAtAcceptance)
            || plan.CandidatePoints != 0 || award.Pending || award.Applied != 0 || award.Suppressed != 0
            || plan.EarnedClawbackPoints != 0
            || disposition == OrderBillingEarningDisposition.Unevaluated && plan.EarningRetired != true
            || disposition != OrderBillingEarningDisposition.Unevaluated && plan.EarningRetired != false)
        {
            throw Invalid("An ineligible or unevaluated earning result cannot carry award points.");
        }
    }

    private static AwardTotals ReadAwardTotals(
        OrderAmendmentLoyaltyPlan plan, OrderBillingAwardWitness? witness)
    {
        if (!plan.AwardPending)
            return new(false, plan.AppliedAwardPoints, plan.SuppressedPoints);
        return witness is null
            ? new(true, plan.AppliedAwardPoints, plan.SuppressedPoints)
            : new(false, witness.AppliedPoints, witness.SuppressedPoints);
    }

    private static OrderAmendmentLoyaltyOperationStatus ReadOperationStatus(
        OrderAmendmentResolutionOperation operation, OrderAmendmentLoyaltyPlan plan,
        OrderAmendmentLoyaltyReservation? reservation, bool ownerAvailable,
        bool awardPending, bool hasEffect)
    {
        if (operation.State == OrderAmendmentResolutionOperationState.Resolved)
            return hasEffect
                ? OrderAmendmentLoyaltyOperationStatus.Resolved
                : OrderAmendmentLoyaltyOperationStatus.None;
        return ReadStatus(plan, reservation, ownerAvailable, awardPending, hasEffect);
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
            if (expected.PlanFingerprint != OrderAmendmentLoyaltyPlanFingerprint.CreateCompensation(
                    operation.SourceOrderId, operation.AmendmentId, expected))
                throw Invalid("A saved loyalty compensation plan fingerprint is invalid.");
        }
    }

    private static void ValidateCompensationUnits(OrderAmendmentResolutionOperation operation,
        OrderAmendmentLoyaltyPlan plan, IReadOnlyList<OrderAmendmentLoyaltyCompensation> compensations,
        IReadOnlyList<OrderAmendmentLoyaltyCompensationUnit> units)
    {
        var acceptedUnitIds = plan.RemovedUnits.Select(value => value.SnapshotUnitId).ToHashSet();
        foreach (var expected in plan.Compensations)
        {
            var header = compensations.Single(value => value.OriginalTransactionId == expected.OriginalTransactionId
                && value.Kind == expected.Kind);
            var stored = units.Where(value => value.CompensationId == header.Id).ToArray();
            if (!OrderAmendmentLoyaltyCompensationPoster.StoredUnitsMatch(stored,
                    operation.SourceOrderId, expected.Kind, expected.Units, acceptedUnitIds)
                || stored.Sum(value => (long)value.Points) != expected.RequiredPoints)
                throw Invalid("The saved loyalty compensation units differ from the reviewed plan.");
        }
        if (units.Any(value => compensations.All(header => header.Id != value.CompensationId)))
            throw Invalid("A saved loyalty compensation unit has no matching header.");
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

    private static int SumRequired(IReadOnlyList<OrderAmendmentLoyaltyCompensation> headers,
        OrderAmendmentLoyaltyCompensationKind kind) => checked((int)headers
            .Where(value => value.Kind == kind).Sum(value => (long)value.RequiredPoints));

    private static bool IsCanonicalEmptyPlan(OrderAmendmentLoyaltyPlan plan, string currency) =>
        string.Equals(plan.Currency, currency, StringComparison.Ordinal)
        && plan.CandidatePoints == 0 && plan.AppliedAwardPoints == 0 && !plan.AwardPending
        && plan.EarnedClawbackPoints == 0 && plan.RedemptionRestorationPoints == 0
        && plan.RemovedUnits is { Count: 0 } && plan.Compensations is { Count: 0 }
        && plan.PendingAwardSuppressions is { Count: 0 }
        && plan.EarningOwnerLinkId is null && plan.RedemptionOwnerLinkId is null
        && plan.SuppressedPoints == 0 && plan.EarningDisposition is null && plan.EarningRetired is null;

    private static ConflictException Invalid(string message) => new(message);
}
