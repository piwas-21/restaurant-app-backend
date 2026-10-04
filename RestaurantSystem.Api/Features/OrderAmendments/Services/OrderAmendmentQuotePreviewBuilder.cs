using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed class OrderAmendmentQuotePreviewBuilder(
    OrderAmendmentSupplementBuilder supplements,
    OrderAmendmentChangeBuilder changes,
    IOrderMappingService mapping) : IOrderAmendmentQuotePreviewBuilder
{
    public Task<OrderDto> MapSourceAsync(Order source, CancellationToken cancellationToken) =>
        mapping.MapToOrderDtoAsync(source, cancellationToken);

    public async Task<OrderAmendmentQuotePreview> BuildAsync(Order source, OrderDto sourceDto,
        OrderAmendmentQuoteRequest request, CancellationToken cancellationToken)
    {
        var supplement = await supplements.BuildAsync(source, request, cancellationToken);
        // A rolled-back preview cannot reserve a human-facing daily order number.
        if (supplement is not null)
            supplement.OrderNumber = string.Empty;
        var supplementDto = supplement is null ? null : OrderAmendmentSnapshots.MapBuiltOrder(mapping, supplement);
        var changeSnapshots = await changes.BuildAsync(source, sourceDto, request, supplementDto, cancellationToken);
        return new(supplement, supplementDto, changeSnapshots);
    }
}
