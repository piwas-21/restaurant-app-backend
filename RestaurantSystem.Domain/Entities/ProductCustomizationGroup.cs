using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>An explicitly ordered choice group on one product.</summary>
public class ProductCustomizationGroup : Entity
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsRequired { get; set; }
    public int MinSelection { get; set; }
    public int MaxSelection { get; set; } = 1;
    public int IncludedFreeUnits { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual Product Product { get; set; } = null!;
    public virtual ICollection<ProductCustomizationGroupDescription> Descriptions { get; set; } = [];
    public virtual ICollection<ProductCustomizationIngredientOption> IngredientOptions { get; set; } = [];
    public virtual ICollection<ProductCustomizationProductOption> ProductOptions { get; set; } = [];
}
