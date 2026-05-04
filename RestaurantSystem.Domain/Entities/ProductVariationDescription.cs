using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Multilingual description for a product variation
/// </summary>
public class ProductVariationDescription : Entity
{
    public Guid ProductVariationId { get; set; }
    public string LanguageCode { get; set; } = null!; // e.g., "en", "tr", "de"
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    // Navigation properties
    public virtual ProductVariation ProductVariation { get; set; } = null!;
}
