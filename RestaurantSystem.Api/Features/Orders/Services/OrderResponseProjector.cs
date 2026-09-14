using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Maps a staff-order response and attaches permissions for the current caller only.</summary>
public sealed class OrderResponseProjector : IOrderResponseProjector
{
    private readonly IOrderMappingService _mapping;
    private readonly IOrderPermittedActionsService _actions;

    public OrderResponseProjector(
        IOrderMappingService mapping, IOrderPermittedActionsService actions)
    {
        _mapping = mapping;
        _actions = actions;
    }

    public async Task<OrderDto> ProjectAsync(Order order, CancellationToken cancellationToken)
    {
        var dto = await _mapping.MapToOrderDtoAsync(order, cancellationToken);
        dto.PermittedActions = _actions.GetPermittedActions(order);
        return dto;
    }
}
