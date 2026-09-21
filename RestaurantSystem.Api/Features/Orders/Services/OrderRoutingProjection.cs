using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class OrderRoutingProjection
{
    internal static List<OrderRoutingStateDto>? Map(Order order) => order.RoutingStates.Count == 0
        ? null
        : order.RoutingStates
            .OrderBy(state => state.Target)
            .Select(state => new OrderRoutingStateDto(
                state.Id, state.JobId, state.Revision, state.Target, state.Status, state.DeviceId,
                state.FailureReason, state.LastAcknowledgedAt, state.Version))
            .ToList();
}
