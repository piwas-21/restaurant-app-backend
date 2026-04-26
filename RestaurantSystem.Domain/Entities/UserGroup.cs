using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class UserGroup : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string QRCodeData { get; set; } = string.Empty; // Unique identifier for the group
    public bool IsActive { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }

    // Navigation properties
    public ICollection<GroupMembership> Memberships { get; set; } = new List<GroupMembership>();
    public ICollection<GroupDiscount> Discounts { get; set; } = new List<GroupDiscount>();
}
