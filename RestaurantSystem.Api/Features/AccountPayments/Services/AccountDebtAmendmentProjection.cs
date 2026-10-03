using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Removes only frozen source units voided by committed native amendments.</summary>
internal static class AccountDebtAmendmentProjection
{
    internal static IReadOnlyList<AccountDebtSegment> ExcludeVoidedUnits(
        IReadOnlyList<Order> orders, IReadOnlyList<AccountDebtSegment> due,
        IReadOnlyList<OrderAmendment> amendments)
    {
        if (amendments.Count == 0)
            return due;

        var ordersById = orders.ToDictionary(order => order.Id);
        var removedRanges = new Dictionary<(Guid OrderId, Guid ItemId), List<(int Start, long End)>>();
        var instructionItems = new HashSet<(Guid OrderId, Guid ItemId)>();
        foreach (var amendment in amendments.Where(value => value.State == OrderAmendmentState.Committed))
        {
            if (!ordersById.TryGetValue(amendment.SourceOrderId, out var source))
                throw InvalidAmendment();
            foreach (var change in ReadChanges(amendment.ChangesJson))
                AddChange(source, change, removedRanges, instructionItems);
        }

        foreach (var ranges in removedRanges.Values)
        {
            var ordered = ranges.OrderBy(range => range.Start).ToArray();
            for (var index = 1; index < ordered.Length; index++)
                if (ordered[index].Start < ordered[index - 1].End)
                    throw InvalidAmendment();
        }

        return due.SelectMany(segment => KeepUnchangedUnits(segment, removedRanges)).ToArray();
    }

    private static void AddChange(
        Order source, OrderAmendmentChangeSnapshot change,
        Dictionary<(Guid OrderId, Guid ItemId), List<(int Start, long End)>> removedRanges,
        HashSet<(Guid OrderId, Guid ItemId)> instructionItems)
    {
        var key = (source.Id, change.OrderItemId);
        var matchingLines = source.Items.Where(item => item.Id == change.OrderItemId
            && item.OrderId == source.Id && !item.ParentOrderItemId.HasValue).Take(2).ToArray();
        var line = matchingLines.Length == 1 ? matchingLines[0] : null;
        if (!Enum.IsDefined(change.Kind) || line is null || line.Quantity <= 0
            || change.Previous is null || change.Previous.Id != line.Id)
            throw InvalidAmendment();

        if (change.Kind == OrderAmendmentChangeKind.InstructionChange)
        {
            if (!change.WholeLine || change.StartOrdinal != 0 || change.Quantity != 0
                || change.Current is null || change.Current.Id != line.Id
                || change.Previous.Quantity != line.Quantity || change.Current.Quantity != line.Quantity
                || removedRanges.ContainsKey(key) || !instructionItems.Add(key))
                throw InvalidAmendment();
            return;
        }

        if (change.Kind is not (OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace)
            || change.WholeLine || change.StartOrdinal < 1 || change.Quantity < 1
            || change.Previous.Quantity != change.Quantity
            || (change.Kind == OrderAmendmentChangeKind.Void && change.Current is not null)
            || (change.Kind == OrderAmendmentChangeKind.Replace
                && (change.Current is null || change.Current.Id == Guid.Empty))
            || instructionItems.Contains(key))
            throw InvalidAmendment();

        var end = (long)change.StartOrdinal + change.Quantity;
        if (end > (long)line.Quantity + 1)
            throw InvalidAmendment();
        if (!removedRanges.TryGetValue(key, out var ranges))
        {
            ranges = [];
            removedRanges.Add(key, ranges);
        }
        ranges.Add((change.StartOrdinal, end));
    }

    private static List<OrderAmendmentChangeSnapshot> ReadChanges(string json)
    {
        try
        {
            return OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(json);
        }
        catch (JsonException)
        {
            throw InvalidAmendment();
        }
    }

    private static IEnumerable<AccountDebtSegment> KeepUnchangedUnits(
        AccountDebtSegment segment,
        Dictionary<(Guid OrderId, Guid ItemId), List<(int Start, long End)>> removedRanges)
    {
        if (segment.OrderItemId is not Guid itemId
            || !removedRanges.TryGetValue((segment.OrderId, itemId), out var ranges))
        {
            yield return segment;
            yield break;
        }

        var cursor = (long)segment.StartOrdinal;
        foreach (var range in ranges.OrderBy(value => value.Start))
        {
            if (range.End <= cursor)
                continue;
            if (range.Start >= segment.EndExclusive)
                break;
            if (range.Start > cursor)
                yield return Slice(segment, cursor, range.Start);
            cursor = Math.Max(cursor, range.End);
            if (cursor >= segment.EndExclusive)
                yield break;
        }

        if (cursor < segment.EndExclusive)
            yield return Slice(segment, cursor, segment.EndExclusive);
    }

    private static AccountDebtSegment Slice(AccountDebtSegment source, long start, long end) => source with
    {
        StartOrdinal = checked((int)start),
        Count = checked((int)(end - start))
    };

    private static ConflictException InvalidAmendment() => new(
        "A committed order amendment does not match the frozen account units. Reconciliation is required.");
}
