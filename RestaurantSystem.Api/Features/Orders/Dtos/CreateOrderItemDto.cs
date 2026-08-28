
using RestaurantSystem.Domain.Common.Enums;

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

    // The ingredient SELECTION — the ids that ARE on the dish — with the same meaning as
    // BasketItemDto.SelectedIngredients. Its PRESENCE is what makes a line SERVER-PRICED: see
    // OrderItemFactory.AddProductItemRecursiveAsync. An empty list is a real answer ("every optional
    // ingredient off"), which is why the trigger is null-vs-not and not Count.
    //
    // IngredientQuantities alone cannot stand in for it. That map is a per-ingredient COUNT, and an
    // ingredient the caller never mentioned is absent from it — indistinguishable from one the caller
    // deliberately removed. BasketToOrderTranslator resolves that ambiguity by ZEROING the deselected
    // (BasketToOrderTranslator.cs:176-197), which is a lossy encoding of an answer the basket held in
    // two fields. This DTO now holds the same two fields, so the order path stops guessing.
    //
    // NOT set by BasketToOrderTranslator, deliberately: the basket has already settled its own money
    // and re-pricing it here would make a second authority out of a field. Null therefore means
    // "priced as before" for every caller that exists today, which is what keeps the anonymous path
    // byte-identical.
    public List<Guid>? SelectedIngredientIds { get; set; }

    // For Menu Bundles
    public List<CreateOrderItemDto>? ChildItems { get; set; }

    // What a CHILD row is (see OrderItemKind). Set by BasketToOrderTranslator, which knows because
    // its two AddRange calls each build exactly one kind; persisted so the renderer stops deriving it
    // from the parent's MUTABLE Product.Type (#318). Optional and ignored on a root row: a caller
    // that hand-builds POST /api/orders may leave it null, and the renderer keeps the old derivation
    // as its fallback for those and for every row written before the column existed.
    public OrderItemKind? Kind { get; set; }
}
