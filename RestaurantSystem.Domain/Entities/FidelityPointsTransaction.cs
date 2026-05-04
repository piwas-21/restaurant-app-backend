using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Type of fidelity points transaction
/// </summary>
public enum TransactionType
{
    Earned,           // Points earned from order
    Redeemed,         // Points redeemed for discount
    AdminAdjustment,  // Manual adjustment by admin
    Expired,          // Points expired
    Refunded          // Points refunded due to order cancellation
}

/// <summary>
/// Fidelity points transaction record
/// </summary>
public class FidelityPointsTransaction : Entity
{
    public Guid UserId { get; set; }
    public Guid? OrderId { get; set; } // Nullable for admin adjustments
    public TransactionType TransactionType { get; set; }
    public int Points { get; set; } // Positive for earning, negative for spending
    public decimal? OrderTotal { get; set; } // For reference
    public string? Description { get; set; }
    public DateTime? ExpiresAt { get; set; } // For point expiration feature

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Order? Order { get; set; }
}
