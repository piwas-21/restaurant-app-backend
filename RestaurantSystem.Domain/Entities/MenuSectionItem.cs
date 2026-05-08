using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class MenuSectionItem : Entity
{
    public Guid MenuSectionId { get; set; }
    public Guid ProductId { get; set; }

    public decimal AdditionalPrice { get; set; } // Extra cost for this option
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }

    // Navigation
    public virtual MenuSection MenuSection { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
}
