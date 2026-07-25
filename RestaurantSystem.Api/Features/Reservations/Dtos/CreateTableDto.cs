using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public record CreateTableDto
{
    [Required]
    [MaxLength(10)]
    public string TableNumber { get; set; } = string.Empty;

    [Required]
    [Range(1, 20)]
    public int MaxGuests { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsOutdoor { get; set; }

    // Geometry is optional and interpreted as metres post-migration
    // (FLOOR-PLAN-REVAMP §5.1). The ranges still deliberately admit the
    // legacy pixel-canvas frontend — position on the old 600×500 box and a
    // few-hundred-px marker — so it keeps working until it retires; the
    // create handler coerces either era into sane metres and auto-links the
    // table to the default plan (§5.2, §6). Omitted → the handler derives a
    // seats-based footprint and centres the table on the plan.
    [Range(0, 10000)]
    public decimal? PositionX { get; set; }

    [Range(0, 10000)]
    public decimal? PositionY { get; set; }

    [Range(0.1, 500)]
    public decimal? Width { get; set; }

    [Range(0.1, 500)]
    public decimal? Height { get; set; }

    // Legacy "circle" / blank are normalised to "round" by the handler.
    [MaxLength(20)]
    public string Shape { get; set; } = "round";

    [Range(0, 360)]
    public int Rotation { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
