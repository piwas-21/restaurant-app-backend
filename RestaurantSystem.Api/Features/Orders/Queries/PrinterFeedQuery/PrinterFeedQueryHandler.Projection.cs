using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

public partial class PrinterFeedQueryHandler
{
    private async Task<List<OrderDto>> MapOrdersAsync(
        IReadOnlyCollection<Order> orders, CancellationToken cancellationToken)
    {
        var ids = orders.Select(order => order.Id).ToArray();
        var supplements = await _context.OrderAmendments.AsNoTracking()
            .Where(value => value.State == OrderAmendmentState.Committed
                && value.SupplementOrderId.HasValue && ids.Contains(value.SupplementOrderId.Value))
            .Join(_context.Orders.IgnoreQueryFilters().AsNoTracking(), value => value.SourceOrderId,
                order => order.Id, (value, order) => new
                {
                    SupplementId = value.SupplementOrderId!.Value,
                    Context = new OrderAmendmentPrintContextDto(value.Id, order.Id, order.OrderNumber)
                }).ToDictionaryAsync(value => value.SupplementId, value => value.Context, cancellationToken);
        // The guest status token is a read-only screen secret. It has no business on printer wire.
        var orderDtos = orders.Select(order =>
        {
            var dto = _mappingService.MapToOrderDto(order);
            dto.GuestStatusToken = null;
            if (supplements.TryGetValue(order.Id, out var amendment))
                dto.AmendmentPrintContext = amendment;
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
