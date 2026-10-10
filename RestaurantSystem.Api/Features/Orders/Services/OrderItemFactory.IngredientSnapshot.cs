using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public partial class OrderItemFactory
{
    private List<OrderItemIngredient> BuildIngredientSnapshots(
        IEnumerable<ProductIngredient>? recipe,
        Dictionary<Guid, int>? ingredientQuantities,
        CreateOrderItemDto itemDto,
        bool metadataAreTrusted) =>
        OrderIngredientSnapshot.Build(
            recipe,
            ingredientQuantities,
            _currentUserService.GetAuditIdentifier(),
            metadataAreTrusted ? itemDto.IngredientQuantityBasis : QuantityBasis.Unknown,
            metadataAreTrusted ? itemDto.IngredientConfigurationScope : ConfigurationScope.Unknown,
            metadataAreTrusted ? itemDto.IngredientCompositionRoles : null);
}
