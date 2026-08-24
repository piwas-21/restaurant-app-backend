using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

/// <summary>
/// The fields a signed-in guest may change on their OWN booking
/// (<c>PUT /api/Reservations/{id}/mine</c>).
/// </summary>
/// <remarks>
/// Deliberately NOT <see cref="UpdateReservationDto"/>: that one requires <c>Status</c> and
/// <c>TableId</c> and carries the admin-only <c>Notes</c>, so opening it to a customer would let
/// them confirm their own booking, move themselves onto any table and write staff notes
/// (mobile BACKEND-NOTES item 1). Anything absent from this record is not guest-editable.
/// </remarks>
public record UpdateMyReservationDto
{
    [Required]
    [MaxLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string CustomerEmail { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? CustomerPhone { get; set; }

    /// <summary>The CALENDAR DAY booked — midnight, e.g. <c>2030-05-17T00:00:00Z</c>. Never an instant.</summary>
    [Required]
    public DateTime ReservationDate { get; set; }

    [Required]
    public TimeSpan StartTime { get; set; }

    [Required]
    public TimeSpan EndTime { get; set; }

    [Required]
    [Range(1, 20)]
    public int NumberOfGuests { get; set; }

    [MaxLength(1000)]
    public string? SpecialRequests { get; set; }
}
