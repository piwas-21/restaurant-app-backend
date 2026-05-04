using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Multilingual translation for a global ingredient
/// </summary>
public class GlobalIngredientTranslation : Entity
{
    public Guid GlobalIngredientId { get; set; }
    public string LanguageCode { get; set; } = null!; // e.g., "en", "tr", "de"
    public string Name { get; set; } = null!;

    // Navigation properties
    public virtual GlobalIngredient GlobalIngredient { get; set; } = null!;
}
