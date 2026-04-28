using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Represents a detailed ingredient for a product with optional/pricing information
/// </summary>
public class ProductIngredient : Entity
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = null!; // Default name (fallback)
    public bool IsOptional { get; set; } = false; // Can customer add/remove this ingredient?
    public int MaxQuantity { get; set; } = 1; // Maximum quantity allowed for this ingredient
    public decimal Price { get; set; } = 0; // Additional price if customer adds this optional ingredient
    public bool IsIncludedInBasePrice { get; set; } = false; // If true, price is included in base and deducted when deselected
    public bool IsActive { get; set; } = true; // Is this ingredient currently available?
    public int DisplayOrder { get; set; } = 0; // Order in which to display ingredients

    public Guid? GlobalIngredientId { get; set; } // Optional link to global ingredient definition

    // Navigation properties
    public virtual Product Product { get; set; } = null!;
    public virtual GlobalIngredient? GlobalIngredient { get; set; }
    public virtual ICollection<ProductIngredientDescription> Descriptions { get; set; } = [];
}
