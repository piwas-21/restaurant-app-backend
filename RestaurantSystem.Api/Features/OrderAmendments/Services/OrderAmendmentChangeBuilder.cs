using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentChangeBuilder
{
    internal static async Task<List<OrderAmendmentChangeSnapshot>> BuildAsync(
        Order source,
        OrderDto sourceDto,
        OrderAmendmentQuoteRequest request,
        OrderDto? supplementDto,
        IStaffCounterOrderPricing serverPricing,
        IOrderItemFactory itemFactory,
        IOrderMappingService mapping,
        CancellationToken cancellationToken)
    {
        var replacements = request.Changes
            .Where(change => change.Kind == OrderAmendmentChangeKind.Replace).ToList();
        var supplementOffset = request.Additions.Count;
        var result = new List<OrderAmendmentChangeSnapshot>(request.Changes.Count);
        var lineBuilder = new OrderAmendmentLineSnapshotBuilder(serverPricing, itemFactory, mapping);

        foreach (var change in request.Changes)
        {
            var entity = source.Items.Single(item => item.Id == change.OrderItemId);
            var previous = OrderAmendmentSnapshots.CloneItem(
                OrderAmendmentSnapshots.FindItem(sourceDto, entity.Id));
            if (change.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace)
            {
                if (change.Quantity < previous.Quantity && previous.SideItems is { Count: > 0 })
                    throw new BadRequestException("A composed line can only be amended as a whole quantity range.");
                previous.Quantity = change.Quantity;
                OrderItemDto? current = null;
                if (change.Kind == OrderAmendmentChangeKind.Replace)
                {
                    var replacementIndex = replacements.IndexOf(change);
                    var currentIndex = supplementOffset + replacementIndex;
                    current = supplementDto?.Items.ElementAtOrDefault(currentIndex)
                        ?? throw new BadRequestException("The replacement item could not be priced.");
                }

                var replacementDispatched = change.Kind == OrderAmendmentChangeKind.Replace
                    && request.ReleaseAdditionsToKitchen
                    ? supplementDto
                    : null;
                result.Add(new OrderAmendmentChangeSnapshot(
                    entity.Id, change.Kind, change.StartOrdinal, change.Quantity,
                    false, previous, current,
                    replacementDispatched?.Id,
                    replacementDispatched?.OrderNumber));
                continue;
            }

            var currentInstruction = await lineBuilder.BuildInstructionCurrentAsync(
                previous, change.Current!, cancellationToken);
            result.Add(new OrderAmendmentChangeSnapshot(
                entity.Id, change.Kind, 0, 0, true, previous, currentInstruction));
        }

        return result;
    }
}
