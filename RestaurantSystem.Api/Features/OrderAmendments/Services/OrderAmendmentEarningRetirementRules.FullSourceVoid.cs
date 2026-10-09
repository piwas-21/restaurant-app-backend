using System.Text.Json;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentEarningRetirementRules
{
    private sealed record FullSourceVoidUnitSet(
        IReadOnlyDictionary<Guid, OrderItem> RootItems,
        HashSet<(Guid OrderItemId, int UnitOrdinal)> Expected,
        IReadOnlyList<OrderBillingSnapshotUnit> Frozen);

    private static bool MatchesUnknownFullSourceVoid(
        Order source, OrderAmendment amendment, IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        OrderAmendmentLoyaltyEvidence evidence, out int unitCount)
    {
        unitCount = 0;
        if (!HasUnknownSourceFacts(source, amendment, changes, evidence))
            return false;

        var unitSet = ReadFullSourceVoidUnitSet(source, evidence.Units);
        if (unitSet is null || !MatchesWholeSourceVoid(changes, unitSet)
            || !HasNoPriorRemovalAmendment(source.Id, amendment.Id, evidence.CommittedAmendments))
            return false;

        unitCount = unitSet.Frozen.Count;
        return unitCount > 0;
    }

    private static bool HasUnknownSourceFacts(
        Order source, OrderAmendment amendment, IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        OrderAmendmentLoyaltyEvidence evidence)
    {
        var snapshot = evidence.Snapshot;
        if (source.ExternalReference is not null || snapshot is null)
            return false;
        if (amendment.SourceOrderId != source.Id || amendment.State != OrderAmendmentState.Committed
            || amendment.ServiceSessionId != source.ServiceSessionId || amendment.SupplementOrderId.HasValue
            || !amendment.CommittedAt.HasValue || changes.Count == 0 || source.FidelityPointsEarned != 0)
            return false;
        return HasNoAcceptedAwardEvidence(source, snapshot, evidence);
    }

    private static bool HasNoAcceptedAwardEvidence(
        Order source, OrderBillingSnapshot snapshot, OrderAmendmentLoyaltyEvidence evidence)
    {
        if (snapshot.OrderId != source.Id || snapshot.Id == Guid.Empty
            || snapshot.EarnedPointsCandidate.HasValue
            || snapshot.EarningDisposition is not (null or OrderBillingEarningDisposition.Unevaluated)
            || HasPartialEarningFacts(snapshot))
            return false;
        return !HasAnyAwardEvidence(evidence);
    }

    private static bool HasAnyAwardEvidence(OrderAmendmentLoyaltyEvidence evidence) =>
        evidence.AwardWitness is not null || evidence.AwardCoverage.Count != 0
        || evidence.Suppressions.Count != 0
        || evidence.Transactions.Any(value => value.TransactionType == TransactionType.Earned)
        || HasPriorCompensationEvidence(evidence);

    private static bool HasPriorCompensationEvidence(OrderAmendmentLoyaltyEvidence evidence) =>
        evidence.PriorCompensations.Count != 0 || evidence.PriorUnits.Count != 0
        || evidence.PriorPostings.Count != 0 || evidence.Reservations.Count != 0;

    private static FullSourceVoidUnitSet? ReadFullSourceVoidUnitSet(
        Order source, IReadOnlyList<OrderBillingSnapshotUnit> frozen)
    {
        var roots = source.Items.Where(value => !value.ParentOrderItemId.HasValue).ToArray();
        if (roots.Length == 0 || roots.Any(value => value.Id == Guid.Empty || value.Quantity <= 0)
            || roots.Select(value => value.Id).Distinct().Count() != roots.Length)
            return null;

        var expected = roots.SelectMany(value => Enumerable.Range(1, value.Quantity)
            .Select(ordinal => (value.Id, ordinal))).ToHashSet();
        if (!HasExactFrozenUnits(source.Id, expected, frozen))
            return null;

        return new(roots.ToDictionary(value => value.Id), expected, frozen);
    }

    private static bool HasExactFrozenUnits(
        Guid orderId, HashSet<(Guid Id, int Ordinal)> expected,
        IReadOnlyList<OrderBillingSnapshotUnit> frozen) =>
        frozen.Count == expected.Count
        && frozen.Select(value => (value.OrderItemId, value.UnitOrdinal)).Distinct().Count() == frozen.Count
        && !frozen.Any(value => value.OrderId != orderId || value.Id == Guid.Empty
            || value.EarnedPoints != 0 || !expected.Contains((value.OrderItemId, value.UnitOrdinal)))
        && expected.SetEquals(frozen.Select(value => (value.OrderItemId, value.UnitOrdinal)));

    private static bool MatchesWholeSourceVoid(
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes, FullSourceVoidUnitSet unitSet)
    {
        var selected = new HashSet<(Guid ItemId, int Ordinal)>();
        foreach (var change in changes)
        {
            if (!MatchesRootVoidChange(change, unitSet.RootItems))
                return false;
            var endOrdinal = (long)change.StartOrdinal + change.Quantity;
            for (var ordinal = change.StartOrdinal; ordinal < endOrdinal; ordinal++)
                if (!selected.Add((change.OrderItemId, ordinal)))
                    return false;
        }
        return unitSet.Expected.SetEquals(selected);
    }

    private static bool MatchesRootVoidChange(
        OrderAmendmentChangeSnapshot change, IReadOnlyDictionary<Guid, OrderItem> rootItems)
    {
        if (change.Kind != OrderAmendmentChangeKind.Void || change.Current is not null
            || change.ReplacementDispatchedOrderId.HasValue
            || change.ReplacementDispatchedOrderNumber is not null
            || !rootItems.TryGetValue(change.OrderItemId, out var item))
            return false;
        return change.Previous.Id == item.Id && change.Previous.Quantity == item.Quantity
            && change.StartOrdinal >= 1 && change.Quantity >= 1
            && (long)change.StartOrdinal + change.Quantity <= (long)item.Quantity + 1;
    }

    private static bool HasNoPriorRemovalAmendment(
        Guid sourceOrderId, Guid amendmentId, IReadOnlyList<OrderAmendment> committedAmendments)
    {
        foreach (var prior in committedAmendments.Where(value => value.Id != amendmentId))
        {
            if (prior.SourceOrderId != sourceOrderId || prior.State != OrderAmendmentState.Committed)
                return false;
            try
            {
                var priorChanges = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(
                    prior.ChangesJson);
                if (priorChanges.Any(value => value.Kind is OrderAmendmentChangeKind.Void
                        or OrderAmendmentChangeKind.Replace))
                    return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }
        return true;
    }
}
