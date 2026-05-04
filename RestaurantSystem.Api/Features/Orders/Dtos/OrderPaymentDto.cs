namespace RestaurantSystem.Api.Features.Orders.Dtos;

public record OrderPaymentDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string PaymentMethod { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Status { get; set; } = null!;

    // Transaction details
    public string? TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime PaymentDate { get; set; }

    // Additional payment info
    public string? CardLastFourDigits { get; set; }
    public string? CardType { get; set; }
    public string? PaymentGateway { get; set; }
    public string? PaymentNotes { get; set; }

    // Refund info
    public bool IsRefunded { get; set; }
    public decimal? RefundedAmount { get; set; }
    public DateTime? RefundDate { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? RefundReason { get; set; }
}
