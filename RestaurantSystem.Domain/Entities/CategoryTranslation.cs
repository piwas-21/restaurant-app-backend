using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>A localized name and description stored alongside a tenant category.</summary>
public class CategoryTranslation : Entity
{
    public Guid CategoryId { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public virtual Category Category { get; set; } = null!;
}
