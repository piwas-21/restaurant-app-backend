namespace RestaurantSystem.Api.Features.ServerWorkspace.Dtos;

public sealed record ServerFloorReservationDto
{
    public Guid ReservationId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public DateTime ReservationDate { get; init; }
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
    public int GuestCount { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
}
