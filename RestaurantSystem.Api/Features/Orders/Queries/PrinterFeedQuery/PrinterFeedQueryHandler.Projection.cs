using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

public partial class PrinterFeedQueryHandler
{
    private List<OrderDto> MapOrders(IReadOnlyCollection<Order> orders)
    {
        // The guest status token is a read-only screen secret. It has no business on printer wire.
        var orderDtos = orders.Select(_mappingService.MapToOrderDto).ToList();
        foreach (var dto in orderDtos)
        {
            dto.GuestStatusToken = null;
        }

        return orderDtos;
    }

    private sealed record RoutingContext(string? DeviceId, bool RoutingActivated);
}
