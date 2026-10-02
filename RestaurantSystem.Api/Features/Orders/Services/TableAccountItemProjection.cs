using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Projects stable unit identities without dividing a frozen line's money.</summary>
internal static class TableAccountItemProjection
{
    public static List<TableBillAccountItemDto> Project(IReadOnlyList<OrderDto> orders) =>
        orders.SelectMany(order => order.Items.Select(item => new TableBillAccountItemDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OrderItemId = item.Id,
            ItemSnapshot = item,
            UnitCount = Math.Max(0, item.Quantity)
        })).ToList();
}
