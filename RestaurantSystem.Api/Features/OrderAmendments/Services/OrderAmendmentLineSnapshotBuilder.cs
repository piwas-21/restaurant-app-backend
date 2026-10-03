using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed class OrderAmendmentLineSnapshotBuilder
{
    private readonly IStaffCounterOrderPricing _serverPricing;
    private readonly IOrderItemFactory _itemFactory;
    private readonly IOrderMappingService _mapping;

    internal OrderAmendmentLineSnapshotBuilder(
        IStaffCounterOrderPricing serverPricing,
        IOrderItemFactory itemFactory,
        IOrderMappingService mapping)
    {
        _serverPricing = serverPricing;
        _itemFactory = itemFactory;
        _mapping = mapping;
    }

    internal async Task<OrderItemDto> BuildInstructionCurrentAsync(
        OrderItemDto previous,
        CreateOrderItemDto current,
        CancellationToken cancellationToken)
    {
        EnsureSameIdentity(previous, current);
        var pricedItems = await _serverPricing.PriceAsync([current], cancellationToken);
        var transient = new Order { Id = Guid.NewGuid(), CreatedBy = "OrderAmendmentQuote" };
        var error = await _itemFactory.AddItemAsync(
            transient, pricedItems.Single(), itemsAreServerPriced: true,
            cancellationToken, allowStaffPrices: false);
        if (error is not null)
            throw new BadRequestException(error);

        OrderAmendmentJson.EnsureItemIdentity(transient);
        var mapped = OrderAmendmentSnapshots.MapBuiltOrder(_mapping, transient).Items.Single();
        EnsureSameNestedIdentity(previous, mapped);
        if (!string.Equals(PaidAddOnFingerprint(previous), PaidAddOnFingerprint(mapped),
                StringComparison.Ordinal))
        {
            throw new BadRequestException(
                "A paid customization change must be quoted as a replacement so its price is explicit.");
        }

        PreserveFrozenIdentityAndPrice(previous, mapped);
        return mapped;
    }

    private static void EnsureSameIdentity(OrderItemDto previous, CreateOrderItemDto current)
    {
        if (previous.ProductId != current.ProductId
            || previous.ProductVariationId != current.ProductVariationId
            || previous.MenuID != current.MenuId
            || previous.Quantity != current.Quantity)
        {
            throw new BadRequestException(
                "Instruction changes must keep the original product, variation, menu, and quantity. Use a replacement for priced line changes.");
        }
    }

    private static void EnsureSameNestedIdentity(OrderItemDto previous, OrderItemDto current)
    {
        var oldChildren = previous.SideItems ?? [];
        var newChildren = current.SideItems ?? [];
        if (oldChildren.Count != newChildren.Count)
            throw new BadRequestException("Instruction changes cannot add or remove nested order components.");

        for (var index = 0; index < oldChildren.Count; index++)
        {
            var oldChild = oldChildren[index];
            var newChild = newChildren[index];
            if (oldChild.ProductId != newChild.ProductId
                || oldChild.ProductVariationId != newChild.ProductVariationId
                || oldChild.MenuID != newChild.MenuID
                || oldChild.Quantity != newChild.Quantity
                || oldChild.Kind != newChild.Kind)
            {
                throw new BadRequestException(
                    "Instruction changes cannot change nested component identity or quantity. Use a replacement.");
            }
            EnsureSameNestedIdentity(oldChild, newChild);
        }
    }

    private static void PreserveFrozenIdentityAndPrice(OrderItemDto previous, OrderItemDto current)
    {
        current.Id = previous.Id;
        current.ProductName = previous.ProductName;
        current.VariationName = previous.VariationName;
        current.Quantity = previous.Quantity;
        current.UnitPrice = previous.UnitPrice;
        current.ItemTotal = previous.ItemTotal;
        current.KitchenType = previous.KitchenType;
        current.Kind = previous.Kind;
        var oldChildren = previous.SideItems ?? [];
        var newChildren = current.SideItems ?? [];
        for (var index = 0; index < oldChildren.Count; index++)
            PreserveFrozenIdentityAndPrice(oldChildren[index], newChildren[index]);
    }

    private static string PaidAddOnFingerprint(OrderItemDto item) => string.Join(
        "|",
        (item.IngredientCustomizations ?? [])
            .Where(ingredient => ingredient.IsAddOn && !ingredient.IsRemoved && ingredient.Quantity > 0)
            .OrderBy(ingredient => ingredient.IngredientId)
            .Select(ingredient => $"{ingredient.IngredientId:N}:{ingredient.Quantity}")
            .Concat((item.SideItems ?? []).Select(PaidAddOnFingerprint)));
}
