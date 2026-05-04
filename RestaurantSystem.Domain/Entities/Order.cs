using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class Order : SoftDeleteEntity
{
    public string OrderNumber { get; set; } = null!;
    public Guid? UserId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    // Order Type
    public OrderType Type { get; set; } // Dine-In, Takeaway, Delivery
    public int? TableNumber { get; set; } // For dine-in orders

    // Pricing
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Discount { get; set; }
    public decimal DiscountPercentage { get; set; } // Applied percentage
    public decimal Tip { get; set; }
    public decimal Total { get; set; }

    // Payment Summary (calculated from OrderPayments)
    public decimal TotalPaid { get; set; }
    public decimal RemainingAmount { get; set; }
    public bool IsFullyPaid => RemainingAmount <= 0;

    // Discount Details
    public string? PromoCode { get; set; }
    public bool HasUserLimitDiscount { get; set; }
    public decimal UserLimitAmount { get; set; } // Threshold for discount

    // Fidelity Points & Discounts
    public int FidelityPointsEarned { get; set; } // Points earned from this order
    public int FidelityPointsRedeemed { get; set; } // Points used for discount
    public decimal FidelityPointsDiscount { get; set; } // Discount amount from redeemed points
    public decimal CustomerDiscountAmount { get; set; } // Special customer discount amount
    public Guid? CustomerDiscountRuleId { get; set; } // Reference to discount rule used

    // Status
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }

    // Focus Order Feature
    public bool IsFocusOrder { get; set; }
    public int? Priority { get; set; } // 1-5, where 1 is highest priority
    public string? FocusReason { get; set; }
    public DateTime? FocusedAt { get; set; }
    public string? FocusedBy { get; set; }

    // Timestamps
    public DateTime OrderDate { get; set; }
    public DateTime? EstimatedDeliveryTime { get; set; }
    public DateTime? ActualDeliveryTime { get; set; }

    // Additional Info
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }


    // Navigation properties
    public virtual ApplicationUser? User { get; set; }
    public virtual OrderAddress? DeliveryAddress { get; set; } // One-to-one relationship
    public virtual CustomerDiscountRule? CustomerDiscountRule { get; set; }
    public virtual ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public virtual ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
    public virtual ICollection<OrderPayment> Payments { get; set; } = new List<OrderPayment>();
    public virtual ICollection<FidelityPointsTransaction> FidelityPointsTransactions { get; set; } = new List<FidelityPointsTransaction>();
}
