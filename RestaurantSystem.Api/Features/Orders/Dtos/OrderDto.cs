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

    // Timestamps
    public DateTime OrderDate { get; set; }
    public DateTime? EstimatedDeliveryTime { get; set; }
    public DateTime? ActualDeliveryTime { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }



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
