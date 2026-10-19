using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record CustomerCheckoutLineDto(
    Guid? BasketItemId,
    Guid? ProductId,
    Guid? MenuId,
    Guid? ProductVariationId,
    string Name,
    string? VariationName,
    int Quantity,
    decimal UnitPrice,
    decimal ItemTotal,
    string? SpecialInstructions,
    OrderItemKind? Kind,
    QuantityBasis QuantityBasis,
    ConfigurationScope ConfigurationScope,
    CompositionRole CompositionRole,
    string? PresentationLabel,
    int? PresentationOrder,
    IReadOnlyList<CustomerCheckoutIngredientDto> Ingredients,
    IReadOnlyList<CustomerCheckoutLineDto> Children);

public sealed record CustomerCheckoutIngredientDto(
    Guid IngredientId,
    string Name,
    int Quantity,
    bool IsRemoved,
    bool IsAddOn,
    QuantityBasis? QuantityBasis,
    ConfigurationScope? ConfigurationScope,
    CompositionRole? CompositionRole,
    int? PresentationOrder);
