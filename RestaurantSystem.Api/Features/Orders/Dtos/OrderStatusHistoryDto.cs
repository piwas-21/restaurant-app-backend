namespace RestaurantSystem.Api.Features.Orders.Dtos;

public class OrderStatusHistoryDto
{
    public Guid Id { get; set; }
    public string FromStatus { get; set; } = null!;
    public string ToStatus { get; set; } = null!;
    public string? Notes { get; set; }
    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = null!;
}
