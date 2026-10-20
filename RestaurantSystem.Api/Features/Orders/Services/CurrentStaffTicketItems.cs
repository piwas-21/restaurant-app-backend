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
            var current = original;
            var removed = new HashSet<int>();
            foreach (var change in changes.Where(change => change.OrderItemId == original.Id))
            {
                if (change.Kind == OrderAmendmentChangeKind.InstructionChange && change.Current is not null)
                    current = change.Current;
                else if (change.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace)
                    for (var unit = change.StartOrdinal; unit < change.StartOrdinal + change.Quantity; unit++)
                        removed.Add(unit);
            }
            var quantity = original.Quantity - removed.Count;
            if (quantity <= 0) continue;
            var amount = original.ItemTotal;
            if (quantity != original.Quantity)
            {
                var money = new AccountMoney(currency);
                var minor = money.ToMinor(original.ItemTotal);
                var perUnit = minor / original.Quantity;
                var remainder = minor % original.Quantity;
                var retained = Enumerable.Range(1, original.Quantity).Where(unit => !removed.Contains(unit));
                amount = money.ToMajor(retained.Sum(unit => perUnit + (unit <= remainder ? 1 : 0)));
            }
            result.Add(current with { Id = original.Id, Quantity = quantity, ItemTotal = amount });
        }
        return result;
    }
}
