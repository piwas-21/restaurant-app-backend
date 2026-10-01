using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class ExternalOrderProjection
{
    internal static ExternalOrderDto? Map(Order order) => order.ExternalReference is { } reference
        ? new ExternalOrderDto(reference.Provider, reference.ExternalDisplayId,
            reference.ExternalState, reference.LastEventAt, reference.Currency,
            reference.MerchantTotal, reference.ReportedTax, reference.FulfillmentType, reference.IsSandbox)
        : null;
}
