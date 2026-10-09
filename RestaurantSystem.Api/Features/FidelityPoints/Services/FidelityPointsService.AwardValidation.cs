using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public partial class FidelityPointsService
{
    private static async Task<int> ValidateSuppressionsAsync(
        Guid orderId,
        IReadOnlyList<OrderBillingSnapshotUnit> units,
        List<OrderBillingUnitAwardSuppression> suppressions,
        ApplicationDbContext context,
        AwardSuppressionValidationMode mode,
        CancellationToken cancellationToken)
    {
        if (suppressions.Count == 0 && mode == AwardSuppressionValidationMode.RecordedRowsOnly)
            return 0;

        var unitsById = units.ToDictionary(value => value.Id);
        if (suppressions.Any(value => value.OrderId != orderId
            || value.SnapshotUnitId == Guid.Empty || value.AmendmentId == Guid.Empty
            || value.SuppressedEarnedPoints <= 0 || !unitsById.ContainsKey(value.SnapshotUnitId))
            || suppressions.Select(value => value.SnapshotUnitId).Distinct().Count() != suppressions.Count)
            throw new ConflictException("The loyalty unit suppression history contains invalid rows.");

        var amendmentIds = suppressions.Select(value => value.AmendmentId).Distinct().ToArray();
        var amendmentQuery = context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == orderId);
        amendmentQuery = mode == AwardSuppressionValidationMode.CompletePreAwardCoverage
            ? amendmentQuery.Where(value => value.State == OrderAmendmentState.Committed)
            : amendmentQuery.Where(value => amendmentIds.Contains(value.Id));
        var amendments = await amendmentQuery
            .Select(value => new AwardAmendmentScope(
                value.Id, value.SourceOrderId, value.State, value.ChangesJson))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var unitsByItem = units.GroupBy(value => value.OrderItemId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var removedUnitsByAmendment = new Dictionary<Guid, HashSet<(Guid ItemId, int Ordinal)>>();

        long total = 0;
        foreach (var suppression in suppressions)
        {
            var unit = unitsById[suppression.SnapshotUnitId];
            if (suppression.SuppressedEarnedPoints != unit.EarnedPoints
                || !amendments.TryGetValue(suppression.AmendmentId, out var amendment)
                || amendment.SourceOrderId != orderId
                || amendment.State != OrderAmendmentState.Committed)
                throw new ConflictException("A loyalty suppression does not match its committed amendment scope.");
            if (!removedUnitsByAmendment.TryGetValue(suppression.AmendmentId, out var removedUnits))
            {
                removedUnits = BuildRemovedUnitScope(amendment.ChangesJson, unitsByItem);
                removedUnitsByAmendment.Add(suppression.AmendmentId, removedUnits);
            }
            if (!removedUnits.Contains((unit.OrderItemId, unit.UnitOrdinal)))
                throw new ConflictException("A loyalty suppression does not match its committed amendment scope.");
            total = checked(total + suppression.SuppressedEarnedPoints);
        }

        if (mode == AwardSuppressionValidationMode.CompletePreAwardCoverage)
            ValidateCompleteRemovalCoverage(units, unitsByItem, amendments.Values, suppressions);

        return checked((int)total);
    }

    private static void ValidateCompleteRemovalCoverage(
        IReadOnlyList<OrderBillingSnapshotUnit> units,
        Dictionary<Guid, OrderBillingSnapshotUnit[]> unitsByItem,
        IEnumerable<AwardAmendmentScope> amendments,
        IReadOnlyCollection<OrderBillingUnitAwardSuppression> suppressions)
    {
        var unitsByIdentity = units.ToDictionary(value => (value.OrderItemId, value.UnitOrdinal));
        var expected = new Dictionary<Guid, Guid>();
        foreach (var amendment in amendments)
        {
            var removed = BuildRemovedUnitScope(amendment.ChangesJson, unitsByItem);
            foreach (var identity in removed)
            {
                var unit = unitsByIdentity[identity];
                if (unit.EarnedPoints > 0 && !expected.TryAdd(unit.Id, amendment.Id))
                    throw new ConflictException("Committed amendment scopes overlap a positive loyalty unit.");
            }
        }

        var recorded = suppressions.ToDictionary(value => value.SnapshotUnitId, value => value.AmendmentId);
        if (expected.Count != recorded.Count
            || expected.Any(value => !recorded.TryGetValue(value.Key, out var amendmentId)
                || amendmentId != value.Value))
            throw new ConflictException("Committed positive loyalty removals lack complete suppression evidence.");
    }

    private static HashSet<(Guid ItemId, int Ordinal)> BuildRemovedUnitScope(
        string changesJson,
        Dictionary<Guid, OrderBillingSnapshotUnit[]> unitsByItem)
    {
        List<OrderAmendmentChangeSnapshot> changes;
        try
        {
            changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(changesJson);
        }
        catch (JsonException)
        {
            throw new ConflictException("The committed amendment has invalid loyalty scope evidence.");
        }

        if (changes.Any(change => change is null))
            throw new ConflictException("The committed amendment has an empty loyalty scope entry.");

        var removedUnits = new HashSet<(Guid ItemId, int Ordinal)>();
        foreach (var change in changes.Where(value =>
                     value.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace))
        {
            if (change.OrderItemId == Guid.Empty || change.StartOrdinal < 1 || change.Quantity < 1
                || !unitsByItem.TryGetValue(change.OrderItemId, out var itemUnits))
                throw new ConflictException("The committed amendment has an invalid loyalty removal range.");

            var end = (long)change.StartOrdinal + change.Quantity;
            var selected = itemUnits.Where(unit => unit.UnitOrdinal >= change.StartOrdinal
                && unit.UnitOrdinal < end).ToArray();
            if (selected.Length != change.Quantity
                || selected.Any(unit => !removedUnits.Add((unit.OrderItemId, unit.UnitOrdinal))))
                throw new ConflictException("The committed amendment has overlapping or incomplete loyalty ranges.");
        }
        return removedUnits;
    }

}
