using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Represents a reusable global ingredient definition
/// </summary>
public class GlobalIngredient : SoftDeleteEntity
{
    public string DefaultName { get; set; } = null!; // Fallback name (usually English)
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public virtual ICollection<GlobalIngredientTranslation> Translations { get; set; } = [];
}
