using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Menus;

internal static class MenuBundleSuggestedSideItemMapper
{
    internal static List<MenuBundleSuggestedSideItemDto>? Map(Product? product, OrderType? requestedOrderType)
    {
        var items = product?.SuggestedSideItems
            .Where(side => side.SideItemProduct is { IsDeleted: false })
            .OrderBy(side => side.DisplayOrder)
            .ThenBy(side => side.Id)
            .Select(side => new MenuBundleSuggestedSideItemDto
            {
                Id = side.Id,
                SideItemProductId = side.SideItemProductId,
                SideItemProductName = side.SideItemProduct.Name,
                SideItemBasePrice = side.SideItemProduct.BasePrice,
                SideItemProductType = side.SideItemProduct.Type,
                Availability = OrderTypeAvailability.Resolve(side.SideItemProduct, requestedOrderType),
                Variations = side.SideItemProduct.Variations
                    .Where(variation => !variation.IsDeleted)
                    .OrderBy(variation => variation.DisplayOrder)
                    .ThenBy(variation => variation.Id)
                    .Select(variation => MenuBundleMapper.MapVariation(side.SideItemProduct, variation))
                    .ToList(),
                IsRequired = side.IsRequired,
                DisplayOrder = side.DisplayOrder
            }).ToList();
        return items is { Count: > 0 } ? items : null;
    }
}
