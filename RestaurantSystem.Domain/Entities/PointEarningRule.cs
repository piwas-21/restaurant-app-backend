using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Rule for earning fidelity points based on order amount
/// </summary>
public class PointEarningRule : Entity
{
    public string Name { get; set; } = null!;
    public decimal MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; } // Nullable for "above X"
    public int PointsAwarded { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } // Lower number = higher priority
}
