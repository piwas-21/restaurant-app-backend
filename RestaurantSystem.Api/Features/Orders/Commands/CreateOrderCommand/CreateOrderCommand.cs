using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;

public record CreateOrderCommand : ICommand<ApiResponse<OrderDto>>
{
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

    // Order Items
    public List<CreateOrderItemDto> Items { get; set; } = new();

    // Multiple Payments
    public List<CreateOrderPaymentDto> Payments { get; set; } = new();
}
