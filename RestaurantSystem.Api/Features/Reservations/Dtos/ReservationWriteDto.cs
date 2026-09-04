using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

/// <summary>
/// The fields every reservation WRITE path carries. <see cref="CreateReservationDto"/> is exactly
/// this; <see cref="UpdateReservationDto"/> adds the two only an edit can set.
/// <para>
/// Inheritance, not composition: System.Text.Json serialises inherited properties FLAT, so the
/// JSON both clients already post is unchanged, and DataAnnotations walks the inherited set.
/// (A nested object WOULD change the wire shape — that is why the analogous
/// <c>CreateOrderFromBasketCommand</c> pair is CPD-excluded instead of deduped.)
/// <c>ReservationDtoSchemaShapeTests</c> pins the published schema, because the mobile client is
/// generated from it and nothing else here would notice an <c>allOf</c> appearing.
/// </para>
/// </summary>
public abstract record ReservationWriteDto
{
    [Required]
    [MaxLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>
    /// Optional on EVERY write path, and identical on all of them since #420. Requiredness is a
    /// per-tenant admin setting, enforced in the handlers by <c>EnsureRequiredFieldsPresentAsync</c>
    /// — not by an annotation that cannot see the tenant. No <c>[Phone]</c>: the list SERVES
    /// <c>""</c> for a missing phone and the dashboard round-trips it, and <c>[Phone]</c> rejects
    /// <c>""</c>, which made a phoneless booking visible-but-uneditable.
    /// </summary>
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

    [MaxLength(1000)]
    public string? SpecialRequests { get; set; }
}
