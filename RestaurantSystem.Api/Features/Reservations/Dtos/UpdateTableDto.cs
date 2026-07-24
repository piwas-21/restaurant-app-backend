using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public record UpdateTableDto
{
    [Required]
    [MaxLength(10)]
    public string TableNumber { get; set; } = string.Empty;

    [Required]
    [Range(1, 20)]
    public int MaxGuests { get; set; }

    public bool IsActive { get; set; }

    public bool IsOutdoor { get; set; }

    [Range(0, 10000)]
    public decimal PositionX { get; set; }

    [Range(0, 10000)]
    public decimal PositionY { get; set; }

    // Geometry is optional: `null` means "not supplied, keep what is stored".
    // Non-nullable here made an omitted field indistinguishable from a zero:
    // an omitted shape/rotation silently overwrote the stored value with
    // "circle"/0, while an omitted width/height defaulted to 0 and failed
    // [Range(10, 500)], rejecting the whole save with a 400.
    // The entity columns stay NOT NULL — only the wire contract is optional.
    [Range(10, 500)]
    public decimal? Width { get; set; }

    [Range(10, 500)]
    public decimal? Height { get; set; }

    [MaxLength(20)]
    public string? Shape { get; set; }

    [Range(0, 360)]
    public int? Rotation { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
