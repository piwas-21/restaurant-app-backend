using RestaurantSystem.Domain.Common.Enums;
using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public record UpdateReservationDto
{
    [Required]
    [MaxLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string CustomerEmail { get; set; } = string.Empty;

    [Required]
    [Phone]
    [MaxLength(20)]
    public string CustomerPhone { get; set; } = string.Empty;

    [Required]
    public Guid TableId { get; set; }

    [Required]
    public DateTime ReservationDate { get; set; }

    [Required]
    public TimeSpan StartTime { get; set; }

    [Required]
    public TimeSpan EndTime { get; set; }

    [Required]
    [Range(1, 20)]
    public int NumberOfGuests { get; set; }

    [Required]
    public ReservationStatus Status { get; set; }

    [MaxLength(1000)]
    public string? SpecialRequests { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}
