using RestaurantSystem.Api.Features.DeliveryChannels.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

public partial class PrinterFeedQueryHandler
{
    private List<OrderDto> MapOrders(IReadOnlyCollection<Order> orders)
    {
        // The guest status token is a read-only screen secret. It has no business on printer wire.
        var orderDtos = orders.Select(order =>
        {
            var dto = _mappingService.MapToOrderDto(order);
            dto.GuestStatusToken = null;
            // The device API key authenticates this feed without a human role. External consumers
            // still require explicit source/lifecycle print grants; ordinary wire stays unchanged.
            if (order.ExternalReference is not null)
                dto.PermittedActions = ExternalOrderPrintPolicy.DeviceActions(order);
            return dto;
        }).ToList();

        return orderDtos;
    }

    private sealed record RoutingContext(string? DeviceId, bool RoutingActivated);
}
