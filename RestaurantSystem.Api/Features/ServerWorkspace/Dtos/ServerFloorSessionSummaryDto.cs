namespace RestaurantSystem.Api.Features.ServerWorkspace.Dtos;

public sealed record ServerFloorSessionSummaryDto
{
    public Guid ServiceSessionId { get; init; }
    public int Version { get; init; }
    public DateTime OpenedAt { get; init; }
    public int AgeMinutes { get; init; }
    public string? Currency { get; init; }
    public decimal Total { get; init; }
    public decimal Paid { get; init; }
    public decimal Remaining { get; init; }
    public int ActiveRoundCount { get; init; }
    public int ReadyRoundCount { get; init; }
    public bool CanCollect { get; init; }
    public bool CanClose { get; init; }
    public bool HasLegacyAmbiguity { get; init; }
}
