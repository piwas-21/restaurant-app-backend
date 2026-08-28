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

    /// <summary>
    /// Optional link to the library row this variation was copied from (plan S4, mirroring
    /// <see cref="ProductIngredient.GlobalIngredientId"/>). PROVENANCE, not shared identity: the
    /// name and the translations were copied at pick time, nothing reads the library row afterwards,
    /// and editing it does not propagate. It exists so a later slice can measure real reuse and turn
    /// propagation on against a snapshot-backed order history rather than against the live catalog.
    /// </summary>
    public Guid? GlobalVariationId { get; set; }

    // Navigation properties
    public virtual Product Product { get; set; } = null!;
    public virtual GlobalVariation? GlobalVariation { get; set; }
    public virtual ICollection<ProductVariationDescription> Descriptions { get; set; } = [];
}
