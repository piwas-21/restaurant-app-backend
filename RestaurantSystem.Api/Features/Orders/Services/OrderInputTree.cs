using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class OrderInputTree
{
    internal static IEnumerable<CreateOrderItemDto> Flatten(IEnumerable<CreateOrderItemDto> roots)
    {
        var remaining = new Stack<CreateOrderItemDto>(roots.Reverse());
        while (remaining.TryPop(out var item))
        {
            yield return item;
            if (item.ChildItems is not null)
                foreach (var child in item.ChildItems.AsEnumerable().Reverse()) remaining.Push(child);
        }
    }
}
