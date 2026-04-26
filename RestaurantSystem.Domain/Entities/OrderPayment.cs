using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;
public class OrderPayment : Entity
{
    public Guid OrderId { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }

    // Transaction details
    public string? TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime PaymentDate { get; set; }

    // Additional payment info
    public string? CardLastFourDigits { get; set; }
    public string? CardType { get; set; } // Visa, MasterCard, etc.
    public string? PaymentGateway { get; set; } // Stripe, PayPal, etc.
    public string? PaymentNotes { get; set; }

    // For refunds
    public bool IsRefunded { get; set; }
    public decimal? RefundedAmount { get; set; }
    public DateTime? RefundDate { get; set; }
    public string? RefundReason { get; set; }

    // Navigation property
    public virtual Order Order { get; set; } = null!;
}
