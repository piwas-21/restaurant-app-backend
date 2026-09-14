using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed record StaffExplicitCustomizationResult(
    List<Guid>? SelectedIngredientIds,
    Dictionary<Guid, int>? IngredientQuantities,
    decimal ProductOptionPrice,
    List<CreateOrderItemDto> ProductOptionChildren);

internal static class StaffExplicitCustomization
{
    internal static StaffExplicitCustomizationResult Resolve(Product product, CreateOrderItemDto source)
    {
        var selection = ExplicitCustomizationSelection.Resolve(product, source.CustomizationSelections);
        var usesGroups = product.CustomizationGroups.Any(group => group.IsActive);
        var children = selection.ProductOptions.Select(option => new CreateOrderItemDto
        {
            ProductId = option.Product.Id,
            Quantity = source.Quantity * option.Quantity,
            UnitPrice = option.AdditionalPrice,
            Kind = OrderItemKind.CustomizationOption
        }).ToList();

        return new(
            usesGroups ? selection.SelectedIngredientIds : source.SelectedIngredientIds,
            usesGroups ? selection.IngredientQuantities : source.IngredientQuantities,
            selection.ProductOptions.Sum(option => option.AdditionalPrice),
            children);
    }
}
