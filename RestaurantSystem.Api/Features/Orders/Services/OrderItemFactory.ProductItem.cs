using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public partial class OrderItemFactory
{
    private async Task<OrderItem> CreateProductOrderItemAsync(
        CreateOrderItemDto itemDto,
        Product product,
        OrderItem? parentItem,
        OrderLineIngredientChoice choice,
        bool pricesAreTrusted,
        bool metadataAreTrusted,
        CancellationToken cancellationToken)
    {
        var (unitPrice, variationName) = ResolvePricing(
            itemDto, product, pricesAreTrusted && choice.Price is null);
        var customization = choice.Price ?? ResolveCustomizationPrice(itemDto, pricesAreTrusted);
        if (choice.Price is not null && parentItem is not null && itemDto.Kind == OrderItemKind.SideItem)
            customization *= parentItem.Quantity;

        // Child rows carry UnitPrice for display but ItemTotal = 0, because the parent's ItemTotal
        // already includes the rolled-up combo price. A hand-built child's CustomizationPrice is
        // added to the root total because OrderItem has no separate customization column; it is
        // line-absolute per the DTO contract, so do not multiply it by the child's quantity. The
        // ingredient-choice price above is per parent for SideItem rows and keeps its multiplier.
        var itemTotal = ResolveItemTotal(parentItem, unitPrice, itemDto.Quantity, customization);

        return new OrderItem
        {
            Id = Guid.NewGuid(),
            ParentOrderItemId = parentItem?.Id,
            ProductId = itemDto.ProductId,
            ProductVariationId = itemDto.ProductVariationId,
            MenuId = itemDto.MenuId,
            ProductName = product.Name,
            VariationName = variationName,
            Quantity = itemDto.Quantity,
            UnitPrice = unitPrice,
            ItemTotal = itemTotal,
            SpecialInstructions = itemDto.SpecialInstructions,
            IngredientQuantitiesJson = SerializeIngredients(choice.Quantities),
            // THE FREEZE POINT: ingredient names, quantities, and authored roles are copied onto
            // the order row here so a later catalogue edit cannot rewrite a receipt already printed.
            IngredientSnapshots = BuildIngredientSnapshots(
                product.DetailedIngredients, choice.Quantities, itemDto, metadataAreTrusted),
            ParentOrderItem = parentItem,
            // A kind belongs to a CHILD row. Discard it on a root even if a caller sent one (#318).
            Kind = parentItem != null ? itemDto.Kind : null,
            SectionId = await ResolveChoiceSectionAsync(parentItem, itemDto, cancellationToken),
            MenuSectionItemId = itemDto.MenuSectionItemId,
            SuggestedSideItemId = metadataAreTrusted ? itemDto.SuggestedSideItemId : null,
            ParentComponentOrderItemId = ResolveParentComponentOrderItemId(
                parentItem, itemDto, metadataAreTrusted),
            QuantityBasis = metadataAreTrusted ? itemDto.QuantityBasis ?? QuantityBasis.Unknown : QuantityBasis.Unknown,
            ConfigurationScope = metadataAreTrusted
                ? itemDto.ConfigurationScope ?? ConfigurationScope.Unknown
                : ConfigurationScope.Unknown,
            CompositionRole = metadataAreTrusted
                ? itemDto.CompositionRole ?? CompositionRole.Unknown
                : CompositionRole.Unknown,
            PresentationLabel = metadataAreTrusted ? itemDto.PresentationLabel : null,
            PresentationOrder = metadataAreTrusted ? itemDto.PresentationOrder : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier(),
        };
    }

    private static Guid? ResolveParentComponentOrderItemId(
        OrderItem? parentItem, CreateOrderItemDto itemDto, bool metadataAreTrusted)
    {
        if (!metadataAreTrusted || parentItem is null || itemDto.Kind != OrderItemKind.SideItem)
            return null;

        if (parentItem.CompositionRole == CompositionRole.Dish
            || parentItem.Kind == OrderItemKind.BundleChild && parentItem.MenuSectionItemId.HasValue)
            return parentItem.Id;

        return null;
    }
}
