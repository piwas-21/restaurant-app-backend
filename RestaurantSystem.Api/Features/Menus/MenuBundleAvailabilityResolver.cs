using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Menus;

/// <summary>
/// Resolves a bundle and its choices from current database state; no response cache sits between
/// authoring changes and these reads.
/// </summary>
public static class MenuBundleAvailabilityResolver
{
    public static ItemAvailabilityDto Resolve(Product product, OrderType? requestedOrderType)
    {
        if (product.IsDeleted || !product.IsActive)
        {
            return new ItemAvailabilityDto
            {
                CanOrder = false,
                Reason = AvailabilityReason.Unavailable,
                AllowedOrderTypes = [],
                InheritsOrderTypes = product.AvailableOrderTypes is null
            };
        }

        var ownAvailability = OrderTypeAvailability.Resolve(product, requestedOrderType);
        var allowedOrderTypes = Enum.GetValues<OrderType>()
            .Where(orderType => IsBundleOrderable(product, orderType))
            .ToList();

        if (allowedOrderTypes.Count == 0)
        {
            return new ItemAvailabilityDto
            {
                CanOrder = false,
                Reason = AvailabilityReason.Unavailable,
                AllowedOrderTypes = allowedOrderTypes,
                InheritsOrderTypes = ownAvailability.InheritsOrderTypes
            };
        }

        if (requestedOrderType is null)
        {
            var canOrderInBrowseMode = ownAvailability.CanOrder;
            return new ItemAvailabilityDto
            {
                CanOrder = canOrderInBrowseMode,
                Reason = canOrderInBrowseMode ? ownAvailability.Reason : AvailabilityReason.Unavailable,
                AllowedOrderTypes = allowedOrderTypes,
                InheritsOrderTypes = ownAvailability.InheritsOrderTypes
            };
        }

        if (!ownAvailability.CanOrder)
        {
            return ownAvailability with { AllowedOrderTypes = allowedOrderTypes };
        }

        var canOrder = allowedOrderTypes.Contains(requestedOrderType.Value);
        return new ItemAvailabilityDto
        {
            CanOrder = canOrder,
            Reason = canOrder ? AvailabilityReason.Available : AvailabilityReason.WrongOrderType,
            AllowedOrderTypes = allowedOrderTypes,
            InheritsOrderTypes = ownAvailability.InheritsOrderTypes
        };
    }

    public static ItemAvailabilityDto ResolveOption(
        MenuSectionItem item,
        OrderType? requestedOrderType)
    {
        var product = item.Product;
        if (product is null || product.IsDeleted || !product.IsActive || !HasActiveVariation(item))
        {
            return new ItemAvailabilityDto
            {
                CanOrder = false,
                Reason = AvailabilityReason.Unavailable,
                AllowedOrderTypes = [],
                InheritsOrderTypes = product?.AvailableOrderTypes is null
            };
        }

        var ownAvailability = OrderTypeAvailability.Resolve(product, requestedOrderType);
        var allowedOrderTypes = ownAvailability.AllowedOrderTypes
            .Where(orderType => OrderTypeAvailability.Resolve(product, orderType).CanOrder)
            .ToList();
        return ownAvailability with { AllowedOrderTypes = allowedOrderTypes };
    }

    private static bool IsBundleOrderable(Product product, OrderType orderType)
    {
        if (!OrderTypeAvailability.Resolve(product, orderType).CanOrder || product.MenuDefinition is null)
        {
            return false;
        }

        return product.MenuDefinition.Sections
            .Where(section => section.IsRequired)
            .All(section => section.Items
                .Where(item => IsOptionOrderable(item, orderType))
                .Select(item => item.ProductId)
                .Distinct()
                .Count() >= (section.AllowRepeatedItems ? 1 : Math.Max(1, section.MinSelection)));
    }

    private static bool IsOptionOrderable(MenuSectionItem item, OrderType orderType) =>
        item.Product is { IsDeleted: false, IsActive: true } product
        && HasActiveVariation(item)
        && OrderTypeAvailability.Resolve(product, orderType).CanOrder;

    private static bool HasActiveVariation(MenuSectionItem item) =>
        !item.ProductVariationId.HasValue
        || item.ProductVariation is { IsDeleted: false, IsActive: true };
}
