using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class ProductCategory : Entity
{
    public bool IsPrimary { get; set; } = false; // One category can be marked as primary
    public int DisplayOrder { get; set; }

    // Foreign Keys
    public Guid ProductId { get; set; }
    public Guid CategoryId { get; set; }

    // Navigation properties
    public virtual Product Product { get; set; } = null!;
    public virtual Category Category { get; set; } = null!;

}
