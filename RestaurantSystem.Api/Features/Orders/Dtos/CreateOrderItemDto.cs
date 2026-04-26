using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public record CreateOrderItemDto
{
    public Guid? ProductId { get; set; }
    public Guid? ProductVariationId { get; set; }
    public Guid? MenuId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; } // Base unit price
    public decimal CustomizationPrice { get; set; } // Total customization price for all quantities
    public string? SpecialInstructions { get; set; }
    public Dictionary<Guid, int>? IngredientQuantities { get; set; } // { ingredientId: quantity }

    // For Menu Bundles
    public List<CreateOrderItemDto>? ChildItems { get; set; }
}
