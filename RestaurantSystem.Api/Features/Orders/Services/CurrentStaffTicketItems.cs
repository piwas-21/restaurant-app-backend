using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Applies committed operational edits without changing original order or financial records.</summary>
internal static class CurrentStaffTicketItems
{
    internal static List<OrderItemDto> Project(
        IReadOnlyList<OrderItemDto> items, IReadOnlyList<OrderAmendmentChangeSnapshot> changes, string? currency)
    {
        var result = new List<OrderItemDto>();
        foreach (var original in items)
        {
            var lineChanges = changes.Where(change => change.OrderItemId == original.Id).ToArray();
            var current = lineChanges.LastOrDefault(change =>
                change.Kind == OrderAmendmentChangeKind.InstructionChange && change.Current is not null)?.Current
                ?? original;
            var removed = lineChanges
                .Where(change => change.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace)
                .SelectMany(change => Enumerable.Range(change.StartOrdinal, change.Quantity)).ToHashSet();
            var quantity = original.Quantity - removed.Count;
            if (quantity <= 0) continue;
            result.Add(current with
            {
                Id = original.Id,
                Quantity = quantity,
                ItemTotal = RetainedAmount(original, removed, currency)
            });
        }
        return result;
    }

    private static decimal RetainedAmount(OrderItemDto original, HashSet<int> removed, string? currency)
    {
        if (removed.Count == 0) return original.ItemTotal;
        var money = new AccountMoney(currency);
        var minor = money.ToMinor(original.ItemTotal);
        var perUnit = minor / original.Quantity;
        var remainder = minor % original.Quantity;
        var retained = Enumerable.Range(1, original.Quantity).Where(unit => !removed.Contains(unit));
        return money.ToMajor(retained.Sum(unit => perUnit + (unit <= remainder ? 1 : 0)));
    }
}
