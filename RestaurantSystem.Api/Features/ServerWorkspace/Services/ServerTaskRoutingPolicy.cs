using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

internal static class ServerTaskRoutingPolicy
{
    public static bool HasRequiredException(Order order) =>
        order.IsKitchenReleased && order.RoutingStates.Count == 0
        || order.RoutingStates.Any(state => state.IsRequired && IsException(state));

    public static bool IsException(OrderRoutingState state) =>
        state.Status is DevicePrintStatus.Failed
            or DevicePrintStatus.NotConfigured
            or DevicePrintStatus.Unknown
            or DevicePrintStatus.Skipped;
}
