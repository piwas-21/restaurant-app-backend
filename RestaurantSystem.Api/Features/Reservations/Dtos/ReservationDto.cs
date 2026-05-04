using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public record ReservationDto
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public Guid TableId { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public DateTime ReservationDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int NumberOfGuests { get; set; }
    public ReservationStatus Status { get; set; }
    public string? SpecialRequests { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
