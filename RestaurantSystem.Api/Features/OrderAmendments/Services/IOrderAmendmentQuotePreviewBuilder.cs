using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal interface IOrderAmendmentQuotePreviewBuilder
{
    Task<OrderDto> MapSourceAsync(Order source, CancellationToken cancellationToken);
    Task<OrderAmendmentQuotePreview> BuildAsync(Order source, OrderDto sourceDto,
        OrderAmendmentQuoteRequest request, CancellationToken cancellationToken);
}

internal sealed record OrderAmendmentQuotePreview(
    Order? Supplement, OrderDto? SupplementDto, IReadOnlyList<OrderAmendmentChangeSnapshot> Changes);
