using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentSourceSnapshot(
    Guid OrderId,
    string OrderNumber,
    OrderType Type,
    OrderStatus Status,
    bool IsKitchenReleased,
    Guid? ServiceSessionId,
    int Version,
    string? Currency,
    decimal Total,
    IReadOnlyList<OrderItemDto> Items);

internal sealed record OrderAmendmentSupplementSnapshot(
    Guid OrderId,
    string OrderNumber,
    OrderType Type,
    bool IsKitchenReleased,
    Guid? ServiceSessionId,
    string? Currency,
    decimal Total,
    string PricingFingerprint,
    IReadOnlyList<OrderItemDto> Items);

internal static class OrderAmendmentSnapshots
{
    // Built supplements/instruction drafts already contain their priced item graph and have no
    // persisted order identity to lazy-load. Project them without querying the database for that id.
    internal static OrderDto MapBuiltOrder(IOrderMappingService mapping, Order order) =>
        mapping.MapToOrderDto(order);

    internal static OrderItemDto FindItem(OrderDto order, Guid itemId) =>
        order.Items.SelectMany(Flatten).SingleOrDefault(item => item.Id == itemId)
        ?? throw new InvalidOperationException("The source item was not projected.");

    internal static OrderItemDto CloneItem(OrderItemDto item) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        ProductVariationId = item.ProductVariationId,
        MenuID = item.MenuID,
        ProductName = item.ProductName,
        VariationName = item.VariationName,
        Quantity = item.Quantity,
        UnitPrice = item.UnitPrice,
        ItemTotal = item.ItemTotal,
        SpecialInstructions = item.SpecialInstructions,
        KitchenType = item.KitchenType,
        IngredientCustomizations = item.IngredientCustomizations?.Select(ingredient => new OrderItemIngredientDto
        {
            IngredientId = ingredient.IngredientId,
            IngredientName = ingredient.IngredientName,
            Quantity = ingredient.Quantity,
            IsRemoved = ingredient.IsRemoved,
            IsAddOn = ingredient.IsAddOn,
            QuantityBasis = ingredient.QuantityBasis,
            ConfigurationScope = ingredient.ConfigurationScope,
            CompositionRole = ingredient.CompositionRole,
            PresentationOrder = ingredient.PresentationOrder
        }).ToList(),
        SideItems = item.SideItems?.Select(CloneItem).ToList(),
        SectionId = item.SectionId,
        MenuSectionItemId = item.MenuSectionItemId,
        SuggestedSideItemId = item.SuggestedSideItemId,
        ParentComponentOrderItemId = item.ParentComponentOrderItemId,
        QuantityBasis = item.QuantityBasis,
        ConfigurationScope = item.ConfigurationScope,
        CompositionRole = item.CompositionRole,
        PresentationLabel = item.PresentationLabel,
        PresentationOrder = item.PresentationOrder,
        Kind = item.Kind
    };

    internal static OrderAmendmentSourceSnapshot Source(OrderDto order) => new(
        order.Id, order.OrderNumber, Enum.Parse<OrderType>(order.Type),
        Enum.Parse<OrderStatus>(order.Status), order.IsKitchenReleased,
        order.ServiceSessionId, order.Version, order.Currency, order.Total,
        order.Items.Select(CloneItem).ToList());

    internal static OrderAmendmentSupplementSnapshot Supplement(OrderDto order) => new(
        order.Id, order.OrderNumber, Enum.Parse<OrderType>(order.Type),
        order.IsKitchenReleased, order.ServiceSessionId, order.Currency,
        order.Total, OrderAmendmentJson.PricingFingerprint(order),
        order.Items.Select(CloneItem).ToList());

    internal static void RestoreSupplementIdentity(
        Order supplement, OrderAmendmentSupplementSnapshot snapshot)
    {
        var frozenItems = snapshot.Items.SelectMany(Flatten).ToList();
        var items = supplement.Items.ToList();
        if (items.Count != frozenItems.Count)
        {
            throw new ConflictException("The quoted supplement lines could not be restored. Quote again.");
        }

        supplement.Id = snapshot.OrderId;
        // The quote transaction rolls back its daily number allocation. Keep the fresh number
        // allocated by the commit builder in this same transaction; only UUID identities freeze.
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            item.Id = frozenItems[index].Id;
            item.OrderId = snapshot.OrderId;
            item.ParentOrderItemId = item.ParentOrderItem?.Id;
        }

        if (supplement.DeliveryAddress is not null)
        {
            supplement.DeliveryAddress.OrderId = snapshot.OrderId;
        }

        foreach (var payment in supplement.Payments)
        {
            payment.OrderId = snapshot.OrderId;
        }

        foreach (var history in supplement.StatusHistory)
        {
            history.OrderId = snapshot.OrderId;
        }
    }

    private static IEnumerable<OrderItemDto> Flatten(OrderItemDto item)
    {
        yield return item;
        if (item.SideItems is null)
            yield break;

        foreach (var child in item.SideItems)
            foreach (var nested in Flatten(child))
                yield return nested;
    }
}
