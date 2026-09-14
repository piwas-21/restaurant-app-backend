using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Membership of an existing product ingredient in an explicit choice group.</summary>
public class ProductCustomizationIngredientOption : Entity
{
    public Guid ProductCustomizationGroupId { get; set; }
    public Guid ProductIngredientId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }

    public virtual ProductCustomizationGroup ProductCustomizationGroup { get; set; } = null!;
    public virtual ProductIngredient ProductIngredient { get; set; } = null!;
}
