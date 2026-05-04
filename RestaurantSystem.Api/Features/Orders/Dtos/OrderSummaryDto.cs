namespace RestaurantSystem.Api.Features.Orders.Dtos;

public record OrderSummaryDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string? CustomerName { get; set; }
    public string Type { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string PaymentStatus { get; set; } = null!;
    public decimal Total { get; set; }
    public DateTime OrderDate { get; set; }
    public int ItemCount { get; set; }
    public bool IsFocusOrder { get; set; }
}
