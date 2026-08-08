using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;

public record CreateOrderCommand : ICommand<ApiResponse<OrderDto>>
{
    // [JsonIgnore] because this selects WHOSE money the order spends. The handler resolves
    // `command.UserId ?? _currentUserService.UserId`, and that id chooses which user's fidelity
    // balance RedeemAsync decrements and whose CustomerDiscountRule usage is consumed — so on an
    // ANONYMOUS endpoint a body value let a caller burn a stranger's points as a discount on their
    // own order. No code path assigns this property (the from-basket handler deliberately leaves it
    // at its default and staff callers are authenticated), so binding it was pure attack surface.
    [JsonIgnore]
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

    // No pre-calculated basket totals. They used to live here and OrderPricingService copied
    // Total from BasketTotal verbatim — on an ANONYMOUS endpoint, so `basketTotal: 0` returned a
    // 200 with PaymentStatus=Completed and awarded fidelity points (S0b). Every money field is now
    // derived server-side from Items + Type + the customer's DB-resident discounts. Do not
    // reintroduce a client-supplied total: a caller may say what it wants, never what it owes.

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

    // Set ONLY by CreateOrderFromBasketCommandHandler, whose Items come from the persisted basket
    // via IBasketToOrderTranslator. [JsonIgnore] keeps it out of the request-body schema so a body
    // value cannot bind it — the same technique CreateOrderFromBasketCommand.SessionId uses. It
    // decides whether the item DTOs' UnitPrice/CustomizationPrice are trusted; default false means
    // a hand-built POST /api/orders is priced from the catalogue.
    [JsonIgnore]
    public bool ItemsAreServerPriced { get; set; }

    // Multiple Payments
    public List<CreateOrderPaymentDto> Payments { get; set; } = new();
}
