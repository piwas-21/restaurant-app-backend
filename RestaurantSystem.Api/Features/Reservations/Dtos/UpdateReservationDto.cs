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

    // Identical to `CreateReservationDto` and `UpdateMyReservationDto` now — the admin edit was
    // the ONLY reservation write path that carried `[Required]` and `[Phone]`, i.e. the only one
    // stricter than the guest booking it edits.
    //
    // Both annotations had to go, and each for its own measured reason. The controller is
    // `[ApiController]`, so DataAnnotations run before the handler: `[Required]` (AllowEmptyStrings
    // false) rejected null AND "", and `[Phone]` rejects "" on its own — "The CustomerPhone field
    // is not a valid phone number." Since the list SERVES "" for a missing phone, the dashboard
    // round-tripped a value its own save refused, so once #420 made a phoneless booking visible it
    // would have been uneditable. Visible and uneditable is not an improvement on invisible.
    //
    // Requiredness is a per-tenant admin setting and is now asked of it in the handler, where the
    // other two paths already ask.
    [MaxLength(20)]
    public string? CustomerPhone { get; set; }

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
