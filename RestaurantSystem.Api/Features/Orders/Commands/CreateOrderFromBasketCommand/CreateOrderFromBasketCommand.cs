using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;

/// <summary>
/// Creates an order from the user's persisted basket — the server owns the basket→order item
/// translation (via <c>IBasketToOrderTranslator</c>) instead of the client hand-building the item
/// payload (menu-bundles redesign #157, slice 5). Carries only the customer-checkout order-level
/// fields (the exact set the checkout page posts); staff/POS-only fields (focus order, user-limit
/// discount, explicit UserId) are intentionally absent — they default on the delegated
/// <c>CreateOrderCommand</c>. The handler translates the basket, then delegates to that untouched
/// legacy command for the actual order build.
/// </summary>
public record CreateOrderFromBasketCommand : ICommand<ApiResponse<OrderDto>>
{
    // Basket source — set by the controller from the X-Session-Id header (not client-body trusted).
    public string SessionId { get; set; } = string.Empty;

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    // Order Type — a customer must pick one; required so an omitted value can't silently default.
    [JsonRequired]
    public OrderType Type { get; set; }
    public int? TableNumber { get; set; }

    public string? PromoCode { get; set; }

    // Pre-calculated values from basket (optional - if provided, use these instead of recalculating)
    public decimal? BasketSubTotal { get; set; }
    public decimal? BasketTax { get; set; }
    public decimal? BasketDiscount { get; set; }
    public decimal? BasketCustomerDiscount { get; set; }
    public decimal? BasketTotal { get; set; }

    // Fidelity Points
    public int? PointsToRedeem { get; set; }

    // Tip — optional; absent means no tip (0).
    public decimal? Tip { get; set; }

    public string? Notes { get; set; }

    public CreateOrderDeliveryAddressDto? DeliveryAddress { get; set; }

    public List<CreateOrderPaymentDto> Payments { get; set; } = new();
}
