using RestaurantSystem.Api.Features.FloorPlan.Dtos;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Dtos;

/// <summary>One authoritative, server-timed floor read for the table-service workspace.</summary>
public sealed record ServerFloorSnapshotDto
{
    public DateTime ServerTime { get; init; }
    public DateTimeOffset TenantTime { get; init; }
    /// <summary>
    /// The next reservation boundary which can change a returned table's state. Clients can
    /// schedule a refresh at this instant without polling on every render.
    /// </summary>
    public DateTimeOffset? NextStateChangeAt { get; init; }
    /// <summary>
    /// Digest of authoritative floor state. ServerTime and derived session AgeMinutes are display
    /// values and are deliberately excluded, so a clock tick does not masquerade as a task-feed
    /// cursor change.
    /// </summary>
    public string Version { get; init; } = string.Empty;
    /// <summary>Equals Version; this is a snapshot cursor, not task-feed pagination.</summary>
    public string Cursor { get; init; } = string.Empty;
    public List<FloorPlanDocumentDto> Zones { get; init; } = [];
    public List<ServerFloorTableDto> Tables { get; init; } = [];
}
