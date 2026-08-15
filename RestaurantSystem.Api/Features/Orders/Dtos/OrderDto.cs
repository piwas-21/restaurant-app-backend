namespace RestaurantSystem.Api.Features.Orders.Dtos;

public record OrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public Guid? UserId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    // Order Type
    public string Type { get; set; } = null!;
    public int? TableNumber { get; set; }

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

    // Status
    public string Status { get; set; } = null!;
    public string PaymentStatus { get; set; } = null!;

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

    // Related Data
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderPaymentDto> Payments { get; set; } = new();
    public List<OrderStatusHistoryDto> StatusHistory { get; set; } = new();

}
