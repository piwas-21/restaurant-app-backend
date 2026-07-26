using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

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

    /// <summary>
    /// The order type (channel) this basket is being built for. Server-owned so add-to-basket can
    /// be validated against per-item availability instead of trusting the client.
    /// </summary>
    /// <remarks>
    /// <c>null</c> means the guest has not chosen a channel yet — the dominant browse state — and is
    /// deliberately PERMISSIVE: every add succeeds. Tightening only happens once a channel is set.
    /// <para>
    /// This does NOT change tax timing. BasketPricingService still leaves <c>Tax = 0</c> for order
    /// creation to compute (Swiss rates differ by order type); moving that is a money-path change
    /// with its own verification.
    /// </para>
    /// </remarks>
    public OrderType? OrderType { get; set; }

    // Navigation properties
    public virtual ApplicationUser? User { get; set; }
    public virtual ICollection<BasketItem> Items { get; set; } = new List<BasketItem>();
}
