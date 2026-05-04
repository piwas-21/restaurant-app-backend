using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class MenuItem : Entity
{
    public bool IsAvailable { get; set; } = true;
    public decimal? SpecialPrice { get; set; } // Override regular price if set
    public int? EstimatedQuantity { get; set; }
    public int DisplayOrder { get; set; }
    public int Quantity { get; set; } = 1; // Default quantity when added to basket

    // Foreign Keys
    public Guid MenuId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariationId { get; set; }

    // Navigation properties
    public virtual Menu DailyMenu { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
    public virtual ProductVariation ProductVariation { get; set; } = null!;
}
