using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
/// <remarks>
/// Ported verbatim from the former frontend <c>utils/orderItemsPayload.ts</c> so the money-path
/// transform is owned by the backend, not the client (menu-bundles redesign #157, slice 5). The
/// mapping is byte-identical to that transform:
/// <list type="bullet">
/// <item>Top-level side items AND bundle children both become order child rows.</item>
/// <item>A child's <c>CustomizationPrice</c> is sent as 0 — <c>BasketService</c> already rolled
/// each child's customization price into the parent's <c>UnitPrice</c>, so a non-zero value here
/// would be double-counted into the root <c>ItemTotal</c> by <c>OrderItemFactory</c> (issue #150).</item>
/// <item>Deselected ingredients are zeroed (not dropped) so <c>OrderMappingService</c> can derive
/// <c>IsRemoved</c> for the kitchen ticket.</item>
/// </list>
/// </remarks>
public class BasketToOrderTranslator : IBasketToOrderTranslator
{
    public List<CreateOrderItemDto> Translate(IEnumerable<BasketItemDto> basketItems) =>
        basketItems.Select(MapTopLevelItem).ToList();

    private static CreateOrderItemDto MapTopLevelItem(BasketItemDto item)
    {
        var orderItem = new CreateOrderItemDto
        {
            ProductId = item.ProductId,
            ProductVariationId = item.ProductVariationId,
            MenuId = item.MenuId,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            CustomizationPrice = item.CustomizationPrice,
            SpecialInstructions = item.SpecialInstructions,
            IngredientQuantities = BuildIngredientQuantities(item),
        };

        var childItems = new List<CreateOrderItemDto>();

        // Top-level side items → child rows (pre-existing behaviour, unchanged).
        if (item.SelectedSideItems is { Count: > 0 })
        {
            childItems.AddRange(item.SelectedSideItems.Select(side => new CreateOrderItemDto
            {
                ProductId = side.Id,
                Quantity = side.Quantity,
                UnitPrice = side.Price,
                CustomizationPrice = 0m,
            }));
        }

        // Bundle children (menu options) with their per-option customizations (issue #150).
        if (item.ChildItems is { Count: > 0 })
        {
            childItems.AddRange(item.ChildItems.Select(MapBundleChild));
        }

        if (childItems.Count > 0)
        {
            orderItem.ChildItems = childItems;
        }

        return orderItem;
    }

    // A bundle-child basket item (a menu option chosen in the bundle modal). CustomizationPrice is
    // 0 (see class remarks); the child keeps its own instructions + ingredient customizations and
    // recurses for any nested children.
    private static CreateOrderItemDto MapBundleChild(BasketItemDto child)
    {
        var childItem = new CreateOrderItemDto
        {
            ProductId = child.ProductId,
            ProductVariationId = child.ProductVariationId,
            Quantity = child.Quantity,
            UnitPrice = child.UnitPrice,
            CustomizationPrice = 0m,
            SpecialInstructions = child.SpecialInstructions,
            IngredientQuantities = BuildIngredientQuantities(child),
        };

        if (child.ChildItems is { Count: > 0 })
        {
            childItem.ChildItems = child.ChildItems.Select(MapBundleChild).ToList();
        }

        return childItem;
    }

    /// <summary>
    /// A copy of the item's ingredient-quantity map with every ingredient NOT in
    /// <see cref="BasketItemDto.SelectedIngredients"/> zeroed out — an explicit 0 is how
    /// <c>OrderMappingService</c> derives <c>IsRemoved</c> for the kitchen ticket. Returns
    /// <c>null</c> when the item carries no quantities (the field is then omitted), matching the
    /// former frontend behaviour.
    /// </summary>
    private static Dictionary<Guid, int>? BuildIngredientQuantities(BasketItemDto item)
    {
        if (item.IngredientQuantities is not { Count: > 0 })
        {
            return null;
        }

        var processed = new Dictionary<Guid, int>(item.IngredientQuantities);

        if (item.SelectedIngredients is not null)
        {
            foreach (var ingredientId in processed.Keys.ToList())
            {
                if (!item.SelectedIngredients.Contains(ingredientId))
                {
                    processed[ingredientId] = 0;
                }
            }
        }

        return processed;
    }
}
