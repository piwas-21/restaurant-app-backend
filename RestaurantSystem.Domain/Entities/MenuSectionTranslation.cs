using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>A localized guest-facing label for a menu section.</summary>
public class MenuSectionTranslation : Entity
{
    public Guid MenuSectionId { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public virtual MenuSection MenuSection { get; set; } = null!;
}
