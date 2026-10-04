using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentRefundRange(Guid ItemId, int Start, long End);

internal static class OrderAmendmentRefundScopePlanner
{
    internal static IReadOnlyList<OrderAmendmentRefundRange> RemovalRanges(
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes)
    {
        var ranges = changes.Where(change => change.Kind is OrderAmendmentChangeKind.Void
                or OrderAmendmentChangeKind.Replace)
            .Select(change => new OrderAmendmentRefundRange(change.OrderItemId, change.StartOrdinal,
                checked(change.StartOrdinal + change.Quantity)))
            .OrderBy(value => value.ItemId).ThenBy(value => value.Start).ToArray();
        if (ranges.Length == 0 || ranges.Any(value => value.ItemId == Guid.Empty || value.Start <= 0
                || value.End <= value.Start))
            throw ReconciliationRequired("The committed amendment has no valid food removal scope.");
        return ranges;
    }

    internal static void EnsureNoPriorRemovalRefund(
        IReadOnlyList<OrderAmendmentRefundRange> removals,
        IReadOnlyList<AccountPaymentAllocationReversal> reversals)
    {
        if (removals.Any(removal => reversals.Any(reversal =>
                reversal.OrderItemId == removal.ItemId
                && reversal.StartOrdinal < removal.End
                && removal.Start < (long)reversal.StartOrdinal + reversal.UnitCount)))
            throw ReconciliationRequired("A removed food unit already has captured refund evidence.");
    }

    internal static IEnumerable<OrderAmendmentRefundScope> Intersections(
        AccountPaymentAllocation allocation, IReadOnlyList<AccountPaymentAllocationReversal> reversals,
        IReadOnlyList<OrderAmendmentRefundRange> removals,
        Dictionary<Guid, List<OrderAmendmentRefundRange>> covered)
    {
        if (allocation.OrderItemId is not Guid itemId)
            yield break;
        var allocationEnd = (long)allocation.StartOrdinal + allocation.UnitCount;
        foreach (var removal in removals.Where(value => value.ItemId == itemId))
        {
            var start = Math.Max(allocation.StartOrdinal, removal.Start);
            var end = Math.Min(allocationEnd, removal.End);
            if (start >= end)
                continue;
            EnsureNoDuplicateCoverage(covered, itemId, start, end);
            foreach (var remaining in SubtractReversed(allocation, reversals, start, end))
                yield return new OrderAmendmentRefundScope(allocation.Id, allocation.OrderId,
                    allocation.OrderItemId, remaining.Start, remaining.Count,
                    allocation.MinorPerUnit, checked(allocation.MinorPerUnit * remaining.Count));
        }
    }

    private static List<RangeSlice> SubtractReversed(
        AccountPaymentAllocation allocation, IReadOnlyList<AccountPaymentAllocationReversal> reversals,
        int start, long end)
    {
        var cursor = (long)start;
        var result = new List<RangeSlice>();
        var previousEnd = (long)allocation.StartOrdinal;
        foreach (var reversal in reversals.Where(value => value.AllocationId == allocation.Id)
                     .OrderBy(value => value.StartOrdinal))
        {
            ValidateReversal(allocation, reversal);
            var reversalEnd = (long)reversal.StartOrdinal + reversal.UnitCount;
            if (reversal.StartOrdinal < previousEnd)
                throw ReconciliationRequired("Prior captured allocation refund scopes overlap.");
            previousEnd = reversalEnd;
            if (reversal.StartOrdinal >= end || reversalEnd <= start)
                continue;
            if (cursor < reversal.StartOrdinal)
                result.Add(new RangeSlice(checked((int)cursor), checked(reversal.StartOrdinal - (int)cursor)));
            cursor = Math.Max(cursor, reversalEnd);
        }
        if (cursor < end)
            result.Add(new RangeSlice(checked((int)cursor), checked((int)(end - cursor))));
        return result;
    }

    private static void ValidateReversal(
        AccountPaymentAllocation allocation, AccountPaymentAllocationReversal reversal)
    {
        if (reversal.OrderId != allocation.OrderId || reversal.OrderItemId != allocation.OrderItemId
            || reversal.MinorPerUnit != allocation.MinorPerUnit || reversal.UnitCount <= 0
            || reversal.StartOrdinal < allocation.StartOrdinal
            || (long)reversal.StartOrdinal + reversal.UnitCount
                > (long)allocation.StartOrdinal + allocation.UnitCount
            || reversal.AmountMinor != checked(reversal.MinorPerUnit * reversal.UnitCount))
            throw ReconciliationRequired("A prior captured-scope refund has invalid frozen evidence.");
    }

    private static void EnsureNoDuplicateCoverage(
        Dictionary<Guid, List<OrderAmendmentRefundRange>> covered, Guid itemId, int start, long end)
    {
        if (!covered.TryGetValue(itemId, out var ranges))
        {
            ranges = [];
            covered.Add(itemId, ranges);
        }
        if (ranges.Any(value => value.Start < end && start < value.End))
            throw ReconciliationRequired("Captured allocation scopes overlap the same removed food unit.");
        ranges.Add(new OrderAmendmentRefundRange(itemId, start, end));
    }

    private static ConflictException ReconciliationRequired(string message) => new(message);

    private sealed record RangeSlice(int Start, int Count);
}
