using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Dtos;

public record BasketDto
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string SessionId { get; set; } = null!;
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Discount { get; set; } // Promo code discount
    public decimal CustomerDiscount { get; set; } // Customer-specific discount
    public string? CustomerDiscountName { get; set; } // Name of the applied customer discount
    public decimal Total { get; set; }
    public string? PromoCode { get; set; }
    public int TotalItems { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// The channel this basket is on, or <c>null</c> when the guest has not chosen one (the
    /// permissive browse state). §9.13: without this the client can ASSERT a channel but never
    /// RECONCILE it — nothing on the wire let it ask what the server actually has, so a basket
    /// changed in another tab, or one whose channel the login merge cleared, went unnoticed.
    /// </summary>
    public OrderType? OrderType { get; set; }

    public List<BasketItemDto> Items { get; set; } = new();
}
