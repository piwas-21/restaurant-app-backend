using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Membership of a real product/component in an explicit choice group.</summary>
public class ProductCustomizationProductOption : Entity
{
    public Guid ProductCustomizationGroupId { get; set; }
    public Guid OptionProductId { get; set; }
    public decimal AdditionalPrice { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }

    public virtual ProductCustomizationGroup ProductCustomizationGroup { get; set; } = null!;
    public virtual Product OptionProduct { get; set; } = null!;
}
