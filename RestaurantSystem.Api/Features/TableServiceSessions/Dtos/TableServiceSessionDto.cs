using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Dtos;

/// <summary>Stable table visit metadata plus its member bill.</summary>
public record TableServiceSessionDto
{
    public Guid ServiceSessionId { get; init; }
    public int TableNumber { get; init; }
    public string? Currency { get; init; }
    public string Status { get; init; } = string.Empty;
    public int Version { get; init; }
    public DateTime OpenedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public int RoundCount { get; init; }
    public int AgeMinutes { get; init; }
    public decimal Outstanding { get; init; }
    public TableBillDto Bill { get; init; } = new();
}
