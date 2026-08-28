using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// One translated name for a <see cref="GlobalVariation"/>. Mirrors
/// <see cref="GlobalIngredientTranslation"/>, and carries no description: a variation name is a
/// label on a choice ("Large"), not prose.
/// </summary>
public class GlobalVariationTranslation : Entity
{
    public Guid GlobalVariationId { get; set; }
    public string LanguageCode { get; set; } = string.Empty; // e.g. "en", "fr", "de"
    public string Name { get; set; } = string.Empty;

    // Navigation properties — nullable because EF only fills it when the read asks for it; the
    // relationship stays REQUIRED through the non-nullable FK above.
    public virtual GlobalVariation? GlobalVariation { get; set; }
}
