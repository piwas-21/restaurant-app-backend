using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;

/// <summary>
/// Creates an order from the user's persisted basket — the server owns the basket→order item
/// translation (via <c>IBasketToOrderTranslator</c>) instead of the client hand-building the item
/// payload (menu-bundles redesign #157, slice 5). Carries the same order-level fields as
/// <c>CreateOrderCommand</c> minus <c>Items</c> (those come from the basket); the handler delegates
/// to the untouched <c>CreateOrderCommand</c> for the actual order build.
/// </summary>
public record CreateOrderFromBasketCommand : ICommand<ApiResponse<OrderDto>>
{
    // Basket source — set by the controller from the X-Session-Id header (not client-body trusted).
    public string SessionId { get; set; } = string.Empty;

    public Guid? UserId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    // Order Type
    public OrderType Type { get; set; }
    public int? TableNumber { get; set; }

    // Discount
    public string? PromoCode { get; set; }
    public bool HasUserLimitDiscount { get; set; }
    public decimal UserLimitAmount { get; set; }

    // Pre-calculated values from basket (optional - if provided, use these instead of recalculating)
    public decimal? BasketSubTotal { get; set; }
    public decimal? BasketTax { get; set; }
    public decimal? BasketDiscount { get; set; }
    public decimal? BasketCustomerDiscount { get; set; }
    public decimal? BasketTotal { get; set; }

    // Fidelity Points
    public int? PointsToRedeem { get; set; }

    // Tip
    public decimal Tip { get; set; }

    // Focus Order
    public bool IsFocusOrder { get; set; }
    public int? Priority { get; set; }
    public string? FocusReason { get; set; }

    // Additional Info
    public string? Notes { get; set; }

    public CreateOrderDeliveryAddressDto? DeliveryAddress { get; set; }

    // Multiple Payments
    public List<CreateOrderPaymentDto> Payments { get; set; } = new();
}
