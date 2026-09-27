using System.Text.Json;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public partial class OrderItemFactory
{
    private static decimal ResolveItemTotal(
        OrderItem? parentItem, decimal unitPrice, int quantity, decimal customization)
    {
        if (parentItem is null)
        {
            return (unitPrice * quantity) + customization;
        }

        // Every child row remains zero. Roll a child's customization into the root because an
        // intermediate parent's zero total is deliberately ignored by aggregate pricing.
        var root = parentItem;
        while (root.ParentOrderItem is not null)
        {
            root = root.ParentOrderItem;
        }
        root.ItemTotal += customization;
        return 0m;
    }

    // An explicit UnitPrice is honoured ONLY when the price is trusted — the items came from the
    // persisted basket via IBasketToOrderTranslator (where a bundle's rolled-up unit price and a
    // variation's modifier are already resolved), or the caller is staff AND the line is one the
    // server could not price for itself (#430 — see OrderLineIngredientChoice).
    //
    // For a hand-built POST /api/orders body the price is taken from the catalogue instead. That
    // endpoint is ANONYMOUS, so honouring its UnitPrice let a caller name its own price: posting
    // `unitPrice: 0.01` against a 12.99 product produced a 0.01 order. It is the same defect class
    // as the client-declared BasketTotal that S0b removed, one level further down — closing only
    // the total would have left the line items to say the same untrue thing.
    private static (decimal unitPrice, string? variationName) ResolvePricing(
        CreateOrderItemDto itemDto, Product product, bool pricesAreTrusted)
    {
        if (pricesAreTrusted && itemDto.UnitPrice >= 0)
        {
            string? variationName = null;
            if (itemDto.ProductVariationId.HasValue)
            {
                var variation = product.Variations.FirstOrDefault(
                    v => v.Id == itemDto.ProductVariationId.Value && !v.IsDeleted);
                variationName = variation?.Name;
            }
            return (itemDto.UnitPrice, variationName);
        }

        var basePrice = product.BasePrice;
        if (itemDto.ProductVariationId.HasValue)
        {
            var variation = product.Variations.FirstOrDefault(
                v => v.Id == itemDto.ProductVariationId.Value && !v.IsDeleted);
            if (variation != null)
            {
                return (basePrice + variation.PriceModifier, variation.Name);
            }
        }
        return (basePrice, null);
    }

    // CustomizationPrice is added straight to a line's ItemTotal, so it is a price lever in its own
    // right and is dropped for the same reason as UnitPrice above. A basket-sourced DTO always
    // sends 0 here for child rows, so this costs the real checkout path nothing.
    private static decimal ResolveCustomizationPrice(CreateOrderItemDto itemDto, bool pricesAreTrusted) =>
        pricesAreTrusted ? itemDto.CustomizationPrice : 0m;

    private static string? SerializeIngredients(Dictionary<Guid, int>? ingredientQuantities) =>
        ingredientQuantities != null ? JsonSerializer.Serialize(ingredientQuantities) : null;
}
