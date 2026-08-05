
namespace RestaurantSystem.Api.Features.Orders.Dtos;

public record CreateOrderItemDto
{
    public Guid? ProductId { get; set; }
    public Guid? ProductVariationId { get; set; }
    public Guid? MenuId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; } // Base unit price
    // Customization for the WHOLE line, not per unit: OrderItemFactory adds it once, after
    // UnitPrice * Quantity. The basket stores it per unit for a regular row and folds it into
    // UnitPrice for a bundle row, so BasketToOrderTranslator derives it rather than copying (#312).
    public decimal CustomizationPrice { get; set; }
    public string? SpecialInstructions { get; set; }
    public Dictionary<Guid, int>? IngredientQuantities { get; set; } // { ingredientId: quantity }

    // For Menu Bundles
    public List<CreateOrderItemDto>? ChildItems { get; set; }
}
