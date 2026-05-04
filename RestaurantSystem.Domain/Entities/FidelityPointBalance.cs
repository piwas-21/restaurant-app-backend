using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// User's fidelity points balance
/// </summary>
public class FidelityPointBalance : Entity
{
    public Guid UserId { get; set; }
    public int CurrentPoints { get; set; }
    public int TotalEarnedPoints { get; set; }
    public int TotalRedeemedPoints { get; set; }
    public DateTime LastUpdated { get; set; }

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
}
