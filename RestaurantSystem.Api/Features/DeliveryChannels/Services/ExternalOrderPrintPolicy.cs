using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

/// <summary>Source/lifecycle gates shared by role-aware staff actions and the authenticated device feed.</summary>
internal static class ExternalOrderPrintPolicy
{
    internal static bool Receipt(Order order) => order.ExternalReference?.ExternalState is "ACCEPTED" or "FINISHED"
        && order.Status is not (OrderStatus.Cancelled or OrderStatus.Refunded);

    internal static bool Kitchen(Order order) => order.ExternalReference?.ExternalState == "ACCEPTED"
        && order.IsKitchenReleased && order.Status is not (OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.Completed);

    internal static bool Blocks(Order order, OrderAction action) => action switch
    {
        OrderAction.PrintKitchen => !Kitchen(order),
        OrderAction.PrintReceipt => !Receipt(order),
        _ => false,
    };

    internal static IReadOnlyList<OrderPermittedActionDto> DeviceActions(Order order) =>
    [
        new() { Action = "PrintKitchen", Allowed = Kitchen(order) },
        new() { Action = "PrintReceipt", Allowed = Receipt(order) },
    ];
}
