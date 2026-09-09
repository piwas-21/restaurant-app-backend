using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public record OrderItemIngredientDto
{
    public Guid IngredientId { get; set; }
    public string IngredientName { get; set; } = null!;
    public int Quantity { get; set; }
    public bool IsRemoved { get; set; } // true if customer deselected/removed this ingredient

    // True when this row is a PAID EXTRA the guest opted into (an optional ingredient not included
    // in the base price), false on every base-recipe row. It exists because a fresh add-on carries
    // quantity 1 — wire-identical to a base-recipe default — so a read surface that showed only
    // removals and quantity > 1 hid every ordinary "extra sauce" the guest chose. The display rule
    // a row must pass to count as CHOSEN is: !IsRemoved && Quantity > 0 && (IsAddOn || Quantity > 1).
    // A quantity-0 row with IsAddOn true is an add-on the guest did NOT pick (the backfilled
    // explicit zero), so consumers must keep the Quantity > 0 half.
    //
    // FROZEN like every other fact on the line (OrderItemIngredient.IsAddOn, S1's rule that a past
    // receipt never changes): a later optionality edit or recipe deletion cannot reclass a row that
    // was already rendered once. Rows written before the column existed read false, which is the
    // pre-flag rendering. The pre-S1 fallback (OrderIngredientCustomizations.ProjectRecipe)
    // computes the flag from the live recipe — the only path where that is still the catalog's call.
    public bool IsAddOn { get; set; }
}

public record OrderItemDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariationId { get; set; }
    public Guid? MenuID { get; set; }
    public string ProductName { get; set; } = null!;
    public string? VariationName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ItemTotal { get; set; }
    public string? SpecialInstructions { get; set; }
    public string? KitchenType { get; set; } // FrontKitchen, BackKitchen, or None
    public List<OrderItemIngredientDto>? IngredientCustomizations { get; set; }
    public List<OrderItemDto>? SideItems { get; set; } // Child order items (additionals)
    public OrderItemKind? Kind { get; set; } // Child items only: bundle component vs true side item
}
