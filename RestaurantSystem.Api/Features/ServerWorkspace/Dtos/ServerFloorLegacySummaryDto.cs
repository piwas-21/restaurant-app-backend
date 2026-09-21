namespace RestaurantSystem.Api.Features.ServerWorkspace.Dtos;

/// <summary>
/// Minimal operational projection for unassigned legacy rounds. It deliberately contains no
/// customer or item data: the table is occupied, but the staff member must review the legacy
/// visit before starting a new explicit session.
/// </summary>
public sealed record ServerFloorLegacySummaryDto
{
    public int OrderCount { get; init; }
    public int ActiveOrderCount { get; init; }
    public int ReadyOrderCount { get; init; }
    public decimal Outstanding { get; init; }
}
