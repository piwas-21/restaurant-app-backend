using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentLoyaltyPlanner
{
    internal static OrderAmendmentLoyaltyPlan Build(
        Order source, OrderAmendment amendment, IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        AccountMoney money, OrderAmendmentLoyaltyEvidence evidence)
    {
        if (source.ExternalReference is not null)
            throw Held("Marketplace-held orders cannot use local loyalty compensation.");
        if (source.Tax != 0)
            throw Held("Tax-bearing orders remain held until tax attribution is supported.");
        if (evidence.Snapshot is null)
        {
            if (HasLoyaltyHistory(source, evidence))
                throw Held("Legacy loyalty history has no immutable unit snapshot.");
            return OrderAmendmentLoyaltyPlan.Empty(money.Currency);
        }

        var accepted = ValidateSnapshot(source, money, evidence);
        var removed = SelectRemovedUnits(source, changes, accepted.Units);
        var hasRetirement = evidence.Retirement is not null;
        if (hasRetirement && !OrderAmendmentEarningRetirementRules.MatchesStoredRetirement(
                source, money, evidence, out _))
            throw Held("The frozen earning retirement does not match its full-source amendment evidence.");
        var retiredForCurrentAmendment = evidence.Retirement?.AmendmentId == amendment.Id;
        if (removed.Length > 0 && accepted.EarningDisposition == OrderBillingEarningDisposition.Unevaluated
            && !retiredForCurrentAmendment)
            throw Held("The accepted earning evaluation is still pending.");
        var suppression = ValidateSuppressionEvidence(source, amendment.Id,
            accepted.Units, evidence, removed,
            evidence.AwardWitness is null);
        var priorCompensatedUnits = ValidatePriorCompensation(
            source.Id, accepted.Snapshot.Id, accepted.Units, evidence);
        var award = ValidateAward(source, money, accepted, evidence);
        ValidatePriorRemovalCoverage(accepted.Units, evidence, suppression.PriorRemovedUnitIds,
            priorCompensatedUnits, award);
        var redemption = ValidateRedemption(source, accepted, evidence);

        var plans = new List<OrderAmendmentLoyaltyCompensationPlan>(2);
        var earnedUnits = award.Coverage.Where(value => removed.Any(unit => unit.Id == value.SnapshotUnitId))
            .Select(value => new OrderAmendmentLoyaltyCompensationUnitPlan(
                value.SnapshotUnitId, value.EligibleEarnedPoints)).ToArray();
        AddPlan(plans, accepted.Snapshot.Id,
            accepted.EarningOwnerLink, award.Transaction, award.Witness,
            OrderAmendmentLoyaltyCompensationKind.EarnedClawback, earnedUnits);

        var redemptionUnits = removed.Where(value => value.RedeemedPoints > 0)
            .Select(value => new OrderAmendmentLoyaltyCompensationUnitPlan(value.Id, value.RedeemedPoints))
            .ToArray();
        AddPlan(plans, accepted.Snapshot.Id,
            accepted.RedemptionOwnerLink, redemption.Transaction, null,
            OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration, redemptionUnits);

        var frozenRemoved = removed.OrderBy(value => value.OrderItemId).ThenBy(value => value.UnitOrdinal)
            .Select(value => new OrderAmendmentLoyaltyUnitAllocation(
                value.Id, value.OrderItemId, value.UnitOrdinal, value.EarnedPoints, value.RedeemedPoints))
            .ToArray();
        var plan = new OrderAmendmentLoyaltyPlan(money.Currency, accepted.Snapshot.Id,
            accepted.CandidatePoints, award.AppliedPoints, award.Pending,
            SumPoints(plans, OrderAmendmentLoyaltyCompensationKind.EarnedClawback),
            SumPoints(plans, OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration),
            frozenRemoved, plans, suppression.PendingAwardSuppressions,
            accepted.EarningOwnerLink?.Id, accepted.RedemptionOwnerLink?.Id,
            ReadSuppressedPoints(accepted, suppression.PendingAwardSuppressions, evidence),
            accepted.EarningDisposition, hasRetirement);
        return plan with
        {
            Compensations = plans.Select(value => value with
            {
                PlanFingerprint = OrderAmendmentLoyaltyPlanFingerprint.CreateCompensation(
                    source.Id, amendment.Id, value)
            }).ToArray()
        };
    }

    private static int ReadSuppressedPoints(AcceptedLoyaltySnapshot accepted,
        IReadOnlyList<OrderAmendmentLoyaltyUnitAllocation> pending,
        OrderAmendmentLoyaltyEvidence evidence)
    {
        var suppressed = evidence.AwardWitness?.SuppressedPoints
            ?? checked((int)(evidence.Suppressions.Sum(value => (long)value.SuppressedEarnedPoints)
                + pending.Sum(value => (long)value.EarnedPoints)));
        if (suppressed < 0 || suppressed > accepted.CandidatePoints)
            throw Held("The frozen pre-award suppression total exceeds its earning candidate.");
        return suppressed;
    }

    private static void AddPlan(List<OrderAmendmentLoyaltyCompensationPlan> plans,
        Guid snapshotId,
        OrderBillingSnapshotOwnerLink? owner, FidelityPointsTransaction? original,
        OrderBillingAwardWitness? witness, OrderAmendmentLoyaltyCompensationKind kind,
        OrderAmendmentLoyaltyCompensationUnitPlan[] units)
    {
        if (units.Length == 0)
            return;
        if (owner is null || original is null || owner.Id == Guid.Empty || original.Id == Guid.Empty)
            throw Held("A required loyalty owner or original transaction is unavailable.");
        var required = units.Sum(value => (long)value.Points);
        if (required <= 0 || required > int.MaxValue)
            throw Held("The exact loyalty compensation exceeds its supported point range.");
        var plan = new OrderAmendmentLoyaltyCompensationPlan(snapshotId, owner.Id,
            original.Id, witness?.Id, kind, original.Points, checked((int)required),
            string.Empty, units.OrderBy(value => value.SnapshotUnitId).ToArray());
        plans.Add(plan);
    }

    private static int SumPoints(IReadOnlyList<OrderAmendmentLoyaltyCompensationPlan> plans,
        OrderAmendmentLoyaltyCompensationKind kind) => checked((int)plans
        .Where(value => value.Kind == kind).Sum(value => (long)value.RequiredPoints));

    private static bool HasLoyaltyHistory(Order source, OrderAmendmentLoyaltyEvidence evidence) =>
        source.FidelityPointsEarned != 0 || source.FidelityPointsRedeemed != 0
        || source.FidelityPointsDiscount != 0 || evidence.Transactions.Count > 0;

    private static ConflictException Held(string message) => new(message);

    private static ConflictException Held(string message, Exception innerException) =>
        new(message, innerException);
}
