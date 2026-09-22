namespace RestaurantSystem.Api.Features.ServerWorkspace.Dtos;

/// <summary>Operational table summary; no customer order collection is joined in the browser.</summary>
public sealed record ServerFloorTableDto
{
    public Guid TableId { get; init; }
    public string TableLabel { get; init; } = string.Empty;
    public Guid? ZoneId { get; init; }
    public string? ZoneName { get; init; }
    public bool IsActive { get; init; }
    public bool IsOutdoor { get; init; }
    public int MaxGuests { get; init; }
    public decimal PositionX { get; init; }
    public decimal PositionY { get; init; }
    public decimal Width { get; init; }
    public decimal Height { get; init; }
    public string Shape { get; init; } = "round";
    public int Rotation { get; init; }
    public string State { get; init; } = "Available";
    public int ActiveRoundCount { get; init; }
    public int ReadyRoundCount { get; init; }
    public ServerFloorSessionSummaryDto? Session { get; init; }
    public ServerFloorLegacySummaryDto? Legacy { get; init; }
    public ServerFloorReservationDto? Reservation { get; init; }
    public bool HasLegacyAmbiguity { get; init; }
    public IReadOnlyList<string> PermittedActions { get; init; } = [];
}
