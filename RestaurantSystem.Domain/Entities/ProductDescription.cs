using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class ProductDescription : Entity
{
    public required string Name { get; set; }
    public required string Lang { get; set; }
    public required string Description { get; set; }

    // Foreign key
    public Guid ProductId { get; set; }

    // Navigation properties
    public virtual Product Product { get; set; } = null!;
}
