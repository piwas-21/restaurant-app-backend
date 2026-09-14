using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Shared wire payload for the staff counter quote and create contracts.</summary>
public record StaffCounterOrderRequest
{
    [JsonRequired]
    public OrderType Type { get; set; }
    public int? TableNumber { get; set; }
    public Guid? ServiceSessionId { get; set; }

    // CustomerUserId is the canonical name. CustomerId remains an additive alias for clients that
    // use the existing reservation/customer vocabulary; the handler rejects conflicting values.
    public Guid? CustomerUserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    public string? PromoCode { get; set; }
    public int? PointsToRedeem { get; set; }
    public decimal? Tip { get; set; }
    public string? Notes { get; set; }

    // Staff creation deliberately records no payment. Collecting cash or recording a standalone card
    // is a separate idempotent staff tender operation. PayLater is accepted as an explicit synonym.
    public StaffOrderPaymentState PaymentState { get; set; } = StaffOrderPaymentState.Unpaid;

    [JsonRequired]
    public List<CreateOrderItemDto> Items { get; set; } = [];

    [JsonIgnore]
    public Guid? EffectiveCustomerUserId => CustomerUserId ?? CustomerId;
}
