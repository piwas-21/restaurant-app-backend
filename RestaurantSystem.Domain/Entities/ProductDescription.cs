using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class ProductDescription : Entity
{
    public string Name { get; set; }
    public string Lang { get; set; }
    public string Description { get; set; }

    // Foreign key
    public Guid ProductId { get; set; }

    // Navigation properties
    public virtual Product Product { get; set; } = null!;
}
