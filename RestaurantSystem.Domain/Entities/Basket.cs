using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class Basket : SoftDeleteEntity
{
    public Guid? UserId { get; set; }
    public string SessionId { get; set; } = null!; // For anonymous users
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Discount { get; set; } // Promo code discount
    public decimal CustomerDiscount { get; set; } // Customer-specific discount (from admin discount rules)
    public decimal Total { get; set; }
    public string? PromoCode { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Notes { get; set; }

    // Navigation properties
    public virtual ApplicationUser? User { get; set; }
    public virtual ICollection<BasketItem> Items { get; set; } = new List<BasketItem>();
}
