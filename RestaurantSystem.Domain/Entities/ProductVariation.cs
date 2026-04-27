using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class ProductVariation : SoftDeleteEntity
{
    public string Name { get; set; } = null!; // e.g., "Small", "Medium", "Large"
    public string? Description { get; set; }
    public decimal PriceModifier { get; set; } // Add to base price
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    // Foreign Keys
    public Guid ProductId { get; set; }

    // Navigation properties
    public virtual Product Product { get; set; } = null!;
    public virtual ICollection<ProductVariationDescription> Descriptions { get; set; } = [];
}
