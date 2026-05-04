using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class ProductSideItem : Entity
{
    public bool IsRequired { get; set; } = false;
    public int DisplayOrder { get; set; }

    // Foreign Keys
    public Guid MainProductId { get; set; }
    public Guid SideItemProductId { get; set; }

    // Navigation properties
    public virtual Product MainProduct { get; set; } = null!;
    public virtual Product SideItemProduct { get; set; } = null!;
}
