using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Multilingual description for a product ingredient
/// </summary>
public class ProductIngredientDescription : Entity
{
    public Guid ProductIngredientId { get; set; }
    public string LanguageCode { get; set; } = null!; // e.g., "en", "tr", "de"
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    // Navigation properties
    public virtual ProductIngredient ProductIngredient { get; set; } = null!;
}
