using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class GroupMembership : Entity
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public string UniqueQRCode { get; set; } = string.Empty; // Unique per user-group combination
    public bool IsActive { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // Navigation properties
    public UserGroup Group { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
