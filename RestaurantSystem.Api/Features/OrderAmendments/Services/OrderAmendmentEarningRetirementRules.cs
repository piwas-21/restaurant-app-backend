using System.Text.Json;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentEarningRetirementRules
{
    internal static bool IsEligible(
        Order source, OrderAmendment amendment, IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        OrderAmendmentLoyaltyEvidence evidence, AccountMoney money,
        OrderAmendmentFinancialPreviewDto recalculatedFinancial, out int unitCount)
    {
        unitCount = 0;
        if (evidence.Retirement is not null || evidence.Operations.Count != 0
            || !HasPendingPaidCredit(amendment, recalculatedFinancial)
            || !OrderAmendmentLoyaltyPlanner.HasValidRetirementSnapshot(source, money, evidence)
            || !MatchesUnknownFullSourceVoid(source, amendment, changes, evidence, out unitCount))
        {
            unitCount = 0;
            return false;
        }
        return true;
    }

    internal static bool MatchesRetirement(
        Order source, OrderAmendment amendment, IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        OrderAmendmentLoyaltyEvidence evidence, AccountMoney money, out int unitCount)
    {
        unitCount = 0;
        var retirement = evidence.Retirement;
        if (retirement is null || retirement.OrderId != source.Id
            || retirement.SnapshotId != evidence.Snapshot?.Id || retirement.AmendmentId != amendment.Id
            || !OrderAmendmentLoyaltyPlanner.HasValidRetirementSnapshot(source, money, evidence)
            || !MatchesUnknownFullSourceVoid(source, amendment, changes, evidence, out unitCount)
            || retirement.RetiredUnitCount != unitCount)
        {
            unitCount = 0;
            return false;
        }
        return true;
    }

    internal static bool MatchesStoredRetirement(
        Order source, AccountMoney money, OrderAmendmentLoyaltyEvidence evidence, out int unitCount)
    {
        unitCount = 0;
        var retirement = evidence.Retirement;
        var amendment = evidence.CommittedAmendments.SingleOrDefault(value => value.Id == retirement?.AmendmentId);
        if (retirement is null || amendment is null)
            return false;
        try
        {
            var changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
            return MatchesRetirement(source, amendment, changes, evidence, money, out unitCount);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool MatchesUnknownFullSourceVoid(
        Order source, OrderAmendment amendment, IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        OrderAmendmentLoyaltyEvidence evidence, out int unitCount)
    {
        unitCount = 0;
        var snapshot = evidence.Snapshot;
        if (source.ExternalReference is not null || snapshot is null
            || amendment.SourceOrderId != source.Id || amendment.State != OrderAmendmentState.Committed
            || amendment.ServiceSessionId != source.ServiceSessionId || amendment.SupplementOrderId.HasValue
            || !amendment.CommittedAt.HasValue || changes.Count == 0 || source.FidelityPointsEarned != 0
            || snapshot.OrderId != source.Id || snapshot.Id == Guid.Empty
            || snapshot.EarnedPointsCandidate.HasValue
            || snapshot.EarningDisposition is not (null or OrderBillingEarningDisposition.Unevaluated)
            || HasPartialEarningFacts(snapshot) || evidence.AwardWitness is not null
            || evidence.AwardCoverage.Count != 0 || evidence.Suppressions.Count != 0
            || evidence.Transactions.Any(value => value.TransactionType == TransactionType.Earned)
            || evidence.PriorCompensations.Count != 0 || evidence.PriorUnits.Count != 0
            || evidence.PriorPostings.Count != 0 || evidence.Reservations.Count != 0)
            return false;

        var roots = source.Items.Where(value => !value.ParentOrderItemId.HasValue).ToArray();
        if (roots.Length == 0 || roots.Any(value => value.Id == Guid.Empty || value.Quantity <= 0)
            || roots.Select(value => value.Id).Distinct().Count() != roots.Length)
            return false;
        var expected = roots.SelectMany(value => Enumerable.Range(1, value.Quantity)
                .Select(ordinal => (value.Id, ordinal))).ToHashSet();
        var frozen = evidence.Units;
        if (frozen.Count != expected.Count || frozen.Select(value => (value.OrderItemId, value.UnitOrdinal))
                .Distinct().Count() != frozen.Count
            || frozen.Any(value => value.OrderId != source.Id || value.Id == Guid.Empty
                || value.EarnedPoints != 0 || !expected.Contains((value.OrderItemId, value.UnitOrdinal)))
            || !expected.SetEquals(frozen.Select(value => (value.OrderItemId, value.UnitOrdinal))))
            return false;

        var items = roots.ToDictionary(value => value.Id);
        var selected = new HashSet<(Guid ItemId, int Ordinal)>();
        foreach (var change in changes)
        {
            if (change.Kind != OrderAmendmentChangeKind.Void || change.Current is not null
                || change.ReplacementDispatchedOrderId.HasValue
                || change.ReplacementDispatchedOrderNumber is not null
                || !items.TryGetValue(change.OrderItemId, out var item)
                || change.Previous.Id != item.Id || change.Previous.Quantity != item.Quantity
                || change.StartOrdinal < 1 || change.Quantity < 1
                || (long)change.StartOrdinal + change.Quantity > (long)item.Quantity + 1)
                return false;
            for (var ordinal = change.StartOrdinal; ordinal < (long)change.StartOrdinal + change.Quantity; ordinal++)
                if (!selected.Add((change.OrderItemId, checked((int)ordinal))))
                    return false;
        }
        if (!expected.SetEquals(selected))
            return false;

        foreach (var prior in evidence.CommittedAmendments.Where(value => value.Id != amendment.Id))
        {
            if (prior.SourceOrderId != source.Id || prior.State != OrderAmendmentState.Committed)
                return false;
            try
            {
                var priorChanges = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(prior.ChangesJson);
                if (priorChanges.Any(value => value.Kind is OrderAmendmentChangeKind.Void
                        or OrderAmendmentChangeKind.Replace))
                    return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        unitCount = frozen.Count;
        return unitCount > 0;
    }

    private static bool HasPendingPaidCredit(
        OrderAmendment amendment, OrderAmendmentFinancialPreviewDto recalculatedFinancial)
    {
        try
        {
            var financial = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(
                amendment.FinancialResolutionJson);
            return financial == recalculatedFinancial
                && financial.ResolutionStatus == OrderAmendmentFinancialResolutionStatus.Pending
                && financial.CreditState == OrderAmendmentCreditState.PendingAllocationReview
                && financial.PotentialCreditMinor > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasPartialEarningFacts(OrderBillingSnapshot snapshot) =>
        snapshot.EarningEvaluationVersion is not null || snapshot.EarningRuleSetFingerprint is not null
        || snapshot.EarningRuleId.HasValue || snapshot.EarningRuleName is not null
        || snapshot.EarningRuleMinimumMinor.HasValue || snapshot.EarningRuleMaximumMinor.HasValue
        || snapshot.EarningRulePoints.HasValue || snapshot.EarningRulePriority.HasValue;
}
