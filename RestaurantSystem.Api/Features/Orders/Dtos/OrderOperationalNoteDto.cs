namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>A persisted internal order instruction, never a guest receipt field.</summary>
public record OrderOperationalNoteDto
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public string Text { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public Guid ClientOperationId { get; init; }
}
