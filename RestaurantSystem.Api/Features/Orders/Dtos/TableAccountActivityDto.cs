namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Staff account history, independent of which member rounds still contribute to the bill.</summary>
public record TableAccountActivityDto
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
}
