using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class OrderActionTargetStatusResolver
{
    public static OrderStatus? Resolve(OrderAction action, Order order) => action switch
    {
        OrderAction.Accept => OrderStatus.Confirmed,
        OrderAction.StartPreparing => OrderStatus.Preparing,
        OrderAction.MarkReady => OrderStatus.Ready,
        OrderAction.HandOver when order.Status == OrderStatus.Ready && order.Type == OrderType.Delivery =>
            OrderStatus.OutForDelivery,
        OrderAction.HandOver when order.Status is OrderStatus.Ready or OrderStatus.OutForDelivery =>
            OrderStatus.Completed,
        _ => null
    };
}
