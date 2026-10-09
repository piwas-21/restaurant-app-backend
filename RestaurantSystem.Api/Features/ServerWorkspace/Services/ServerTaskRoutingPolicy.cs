using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Api.Features.KitchenBoard.Services;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

internal static class ServerTaskRoutingPolicy
{
    public static bool HasRequiredException(Order order) =>
        KitchenBoardWorkRules.HasRequiredRoutingException(order);

    public static bool IsException(OrderRoutingState state) =>
        KitchenBoardWorkRules.IsRoutingException(state.Status);
}
