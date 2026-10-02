using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Combines the canonical order mapper with the current caller's queue actions.</summary>
public sealed class OrderQueueProjection : IOrderQueueProjection
{
    private readonly IOrderMappingService _mapping;
    private readonly IOrderPermittedActionsService _permittedActions;

    public OrderQueueProjection(
        IOrderMappingService mapping, IOrderPermittedActionsService permittedActions)
    {
        _mapping = mapping;
        _permittedActions = permittedActions;
    }

    public OrderDto Project(Order order, bool includePermittedActions)
    {
        var dto = _mapping.MapToOrderDto(order);
        return AttachActions(order, dto, includePermittedActions);
    }

    public async Task<OrderDto> ProjectAsync(
        Order order, bool includePermittedActions, CancellationToken cancellationToken)
    {
        var dto = await _mapping.MapToOrderDtoAsync(order, cancellationToken);
        return AttachActions(order, dto, includePermittedActions);
    }

    private OrderDto AttachActions(Order order, OrderDto dto, bool includePermittedActions)
    {
        dto.PermittedActions = includePermittedActions
            ? _permittedActions.GetPermittedActions(order)
            : null;
        return dto;
    }
}
