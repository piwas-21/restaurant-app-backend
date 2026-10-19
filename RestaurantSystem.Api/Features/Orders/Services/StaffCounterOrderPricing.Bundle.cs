using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed partial class StaffCounterOrderPricing
{
    private CreateOrderItemDto PriceBundle(
        CreateOrderItemDto source, Product product, ProductVariation? variation,
        LineCustomization customization, List<CreateOrderItemDto>? children,
        IReadOnlyDictionary<Guid, Product> products, bool sourceIsBasket)
    {
        var sections = product.MenuDefinition?.Sections.ToList()
            ?? throw new NotFoundException("Menu definition not found");
        var (selections, sectionItems) = CollectBundleSelections(source, children ?? [], sections, sourceIsBasket);

        // The same required/min/max/membership rule as the Basket menu path. Quantities passed to
        // this helper are per parent unit; the staff wire keeps child quantities line-absolute.
        var optionsPerParent = MenuBundleSelectionRules.ValidateAndSumOptionPrices(sections, selections);
        var pricedChildren = new List<CreateOrderItemDto>(sectionItems.Count);
        decimal nestedOptionPricesPerParent = 0m;
        foreach (var (child, sectionItem) in sectionItems)
        {
            var childProduct = products.GetValueOrDefault(child.ProductId!.Value)
                ?? throw new NotFoundException($"Product {child.ProductId.Value} not found");
            var childVariation = sourceIsBasket
                ? BundleComponentSelection.ResolveVariation(childProduct, sectionItem, child.ProductVariationId)
                : ResolveVariation(childProduct, child.ProductVariationId);
            BasketBaseProductGuard.EnsureVariationChosen(childProduct, childVariation);
            var pricedChild = PriceLine(child, childProduct, childVariation, products, sourceIsBasket);

            // A bundle section replaces the option's base price, but never its variation modifier
            // (or any composed surcharge already resolved on that option).
            var childUnitPrice = sectionItem.AdditionalPrice
                + (pricedChild.UnitPrice - childProduct.BasePrice);
            nestedOptionPricesPerParent += (pricedChild.UnitPrice
                - childProduct.BasePrice - (sourceIsBasket && !sectionItem.ProductVariationId.HasValue
                    ? 0m : childVariation?.PriceModifier ?? 0m))
                * (child.Quantity / source.Quantity);
            pricedChildren.Add(pricedChild with
            {
                UnitPrice = childUnitPrice,
                NativeBundlePriceVerified = sourceIsBasket,
                MenuSectionItemId = sourceIsBasket ? sectionItem.Id : child.MenuSectionItemId,
                Kind = OrderItemKind.BundleChild
            });
        }

        return source with
        {
            UnitPrice = product.BasePrice + (variation?.PriceModifier ?? 0m)
                + optionsPerParent + nestedOptionPricesPerParent,
            // The parent can carry its own map-only/selection customization. It is per unit in the
            // shared builder and line-absolute in CreateOrderItemDto.
            CustomizationPrice = customization.CustomizationPrice * source.Quantity,
            IngredientQuantities = customization.IngredientQuantities,
            ChildItems = pricedChildren
        };
    }

    private static (List<SelectedMenuOptionDto> Selections, List<(CreateOrderItemDto Child, MenuSectionItem Item)> Rows)
        CollectBundleSelections(CreateOrderItemDto source, List<CreateOrderItemDto> sourceChildren,
            List<MenuSection> sections, bool sourceIsBasket)
    {
        var selections = new List<SelectedMenuOptionDto>(sourceChildren.Count);
        var sectionItems = new List<(CreateOrderItemDto Child, MenuSectionItem Item)>(sourceChildren.Count);
        foreach (var child in sourceChildren)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (!child.ProductId.HasValue)
            {
                throw new BadRequestException("A bundle option must reference a product.");
            }

            if (!child.SectionId.HasValue || child.SectionId.Value == Guid.Empty)
            {
                throw new BadRequestException("A bundle option must identify its menu section.");
            }

            if (child.Quantity <= 0 || child.Quantity % source.Quantity != 0)
            {
                throw new BadRequestException("Bundle option quantity does not match the parent quantity.");
            }

            var optionQuantity = child.Quantity / source.Quantity;
            var sectionItem = ResolveBundleRow(sections, child, sourceIsBasket);
            sectionItems.Add((child, sectionItem));
            selections.Add(new SelectedMenuOptionDto
            {
                SectionId = child.SectionId.Value,
                ItemId = child.ProductId.Value,
                ProductVariationId = sectionItem.ProductVariationId,
                MenuSectionItemId = sectionItem.Id,
                Quantity = optionQuantity,
                SpecialInstructions = child.SpecialInstructions,
                SelectedIngredients = child.SelectedIngredientIds,
                IngredientQuantities = child.IngredientQuantities
            });
        }
        return (selections, sectionItems);
    }

    private static MenuSectionItem ResolveBundleRow(List<MenuSection> sections, CreateOrderItemDto child, bool sourceIsBasket)
    {
        if (!sourceIsBasket || !child.MenuSectionItemId.HasValue)
            return MenuBundleSelectionRules.ResolveSectionItem(sections, child.SectionId!.Value,
                child.ProductId!.Value, child.ProductVariationId);
        var row = sections.Where(section => section.Id == child.SectionId)
            .SelectMany(section => section.Items).SingleOrDefault(item => item.Id == child.MenuSectionItemId)
            ?? throw new BadRequestException("The selected menu-section row is no longer available.");
        return MenuBundleSelectionRules.ResolveSectionItem(sections, child.SectionId!.Value,
            child.ProductId!.Value, row.ProductVariationId, row.Id);
    }
}
