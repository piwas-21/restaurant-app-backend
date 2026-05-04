using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Type of discount
/// </summary>
public enum DiscountType
{
    Percentage,    // Percentage off (e.g., 10%)
    FixedAmount    // Fixed amount off (e.g., $5)
}

/// <summary>
/// Customer-specific discount rule
/// </summary>
public class CustomerDiscountRule : Entity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = null!;
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public int? MaxUsageCount { get; set; } // Limit how many times it can be used
    public int UsageCount { get; set; } // Track usage
    public bool IsActive { get; set; } = true;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
}
