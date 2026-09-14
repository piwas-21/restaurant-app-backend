using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Localized name and description for a product customization group.</summary>
public class ProductCustomizationGroupDescription : Entity
{
    public Guid ProductCustomizationGroupId { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public virtual ProductCustomizationGroup ProductCustomizationGroup { get; set; } = null!;
}
