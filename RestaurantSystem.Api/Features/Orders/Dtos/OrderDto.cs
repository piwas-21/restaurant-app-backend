using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public record OrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Bearer secret for the guest's OWN status poll (order confirmation flows): the checkout
    /// hands it to the confirmation URL, whose polling endpoint accepts id + this token and
    /// returns nothing but lifecycle state. Read-only by construction — the operator-only
    /// QuickActionToken stays out of this DTO on purpose.
    /// <para>
    /// Omitted on the wire when null: the printer feed strips the token before serializing
    /// (a guest secret has no business on paper or spooler logs), and the golden wire snapshot
    /// must stay byte-identical to the pre-feature feed.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GuestStatusToken { get; set; }
    public Guid? UserId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    // Order Type
    public string Type { get; set; } = string.Empty;
    public int? TableNumber { get; set; }
    public Guid? TableId { get; set; }
    public string? TableLabel { get; set; }

    /// <summary>Explicit table-visit membership. Null means a legacy/anonymous order.</summary>
    public Guid? ServiceSessionId { get; set; }

    // Pricing
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Discount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal CustomerDiscountAmount { get; set; }
    public decimal Tip { get; set; }
    public decimal Total { get; set; }

    // Payment Summary
    public decimal TotalPaid { get; set; }
    public decimal RemainingAmount { get; set; }
    public bool IsFullyPaid { get; set; }

    // Staff counter release state. Additive so older clients can ignore it.
    public bool IsKitchenReleased { get; set; }
    public DateTime? KitchenReleasedAt { get; set; }
    public string? KitchenReleasedBy { get; set; }

    /// <summary>Server-issued aggregate version for conditional order mutations.</summary>
    public int Version { get; set; }

    // Status
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OrderPermittedActionDto>? PermittedActions { get; set; }

    // Focus Order
    public bool IsFocusOrder { get; set; }
    public int? Priority { get; set; }
    public string? FocusReason { get; set; }
    public DateTime? FocusedAt { get; set; }
    public string? FocusedBy { get; set; }

    /// <summary>
    /// Staff order-type override, per ORDER-TYPE-AVAILABILITY-PLAN section 9.6. Both are null on an
    /// ordinary order. Additive and read-only: no client is required to render them, but a column
    /// nothing can read is a column nobody trusts.
    /// </summary>
    public string? OrderTypeOverrideBy { get; set; }

    /// <inheritdoc cref="OrderTypeOverrideBy" />
    public string? OrderTypeOverrideItems { get; set; }

    // Timestamps
    public DateTime OrderDate { get; set; }
    public DateTime? EstimatedDeliveryTime { get; set; }
    public DateTime? ActualDeliveryTime { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }



    /// <summary>
    /// The language this order's mails are written in — frozen at creation from the guest's own
    /// request (EMAIL-LOCALISATION-PLAN §1 rank 1, S4), <c>null</c> on every order placed before
    /// that shipped. Read-only, and carried on the DTO because the two mails that need it are sent
    /// from a detached task and from the anonymous resend endpoint, neither of which has the entity.
    /// </summary>
    public string? PreferredLanguage { get; set; }

    // Additional Info
    public string? Notes { get; set; }
    public DeliveryAddressDto? DeliveryAddress { get; set; }
    public string? CancellationReason { get; set; }

    public string? PromoCode { get; set; }
    public bool HasUserLimitDiscount { get; set; }
    public decimal UserLimitAmount { get; set; } // Threshold for discount

    /// <summary>
    /// The currency this order's money amounts are DISPLAYED in (POS plan C18) — never an input
    /// to pricing or rounding. Resolution lives in <see cref="Services.OrderDisplayCurrencyResolver"/>:
    /// the first payment tender carrying a currency (any status) wins, else the tenant's declared
    /// <see cref="RestaurantInfo.Currency"/>, else null — a consumer must not invent a label
    /// (receipts used to hardcode CHF on a EUR tenant's paper). Additive and read-only: older
    /// printer-app / frontend builds ignore it. A tender value is lower-case as Stripe stores it;
    /// consumers render case-insensitively.
    /// </summary>
    public string? Currency { get; set; }

    // Related Data
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderPaymentDto> Payments { get; set; } = new();
    public List<OrderStatusHistoryDto> StatusHistory { get; set; } = new();

    /// <summary>Durable printer-routing state when the staff projection loaded it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OrderRoutingStateDto>? RoutingStates { get; set; }

}
