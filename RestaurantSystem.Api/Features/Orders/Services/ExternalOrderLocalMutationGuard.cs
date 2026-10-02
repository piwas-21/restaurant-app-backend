using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class ExternalOrderLocalMutationGuard
{
    internal static bool AllowsPreparation(Order order, OrderStatus target)
        => order.ExternalReference is { Provider: "uber-eats", ExternalState: "ACCEPTED", FulfillmentType: "DELIVERY_BY_UBER" }
            && order.IsKitchenReleased && order.Status is OrderStatus.Confirmed or OrderStatus.Preparing
            && target is OrderStatus.Preparing or OrderStatus.Ready;

    internal static void RequireStatusMutation(Order order, OrderStatus target, int? expectedVersion, bool isApiToken)
    {
        if (order.ExternalReference is null) return;
        if (isApiToken) throw new ForbiddenException("Marketplace preparation requires a signed-in staff member.");
        if (!AllowsPreparation(order, target)) RequireLocalOrder(order);
        if (!expectedVersion.HasValue)
            throw new BadRequestException("Marketplace preparation requires the current order version.");
    }

    internal static void RequireLocalOrder(Order order)
    {
        if (order.ExternalReference is not null)
            throw new ForbiddenException("This order is managed by its delivery channel. Use the channel decision workflow.");
    }
}
