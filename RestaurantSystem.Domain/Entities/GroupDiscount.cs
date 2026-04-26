using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class GroupDiscount : Entity
{
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DiscountType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MinimumOrderAmount { get; set; }
    public decimal? MaximumDiscountAmount { get; set; }
    public bool IsActive { get; set; }

    // Navigation properties
    public UserGroup Group { get; set; } = null!;
}
