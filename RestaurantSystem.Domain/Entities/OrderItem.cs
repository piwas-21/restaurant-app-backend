using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class OrderItem : Entity
{
    public Guid OrderId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariationId { get; set; }
    public Guid? MenuId { get; set; }
    public Guid? ParentOrderItemId { get; set; }

    public string ProductName { get; set; } = null!; // Snapshot at order time
    public string? VariationName { get; set; } // Snapshot at order time
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; } // Price at order time
    public decimal ItemTotal { get; set; }
    public string? SpecialInstructions { get; set; }
    public string? IngredientQuantitiesJson { get; set; } // JSON: { ingredientId: quantity }

    // Navigation properties
    public virtual Order Order { get; set; } = null!;
    public virtual Product? Product { get; set; }
    public virtual ProductVariation? ProductVariation { get; set; }
    public virtual Menu? Menu { get; set; }
    public virtual OrderItem? ParentOrderItem { get; set; }
    public virtual ICollection<OrderItem> ChildOrderItems { get; set; } = new List<OrderItem>();
}
