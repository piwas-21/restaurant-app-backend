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

    public bool IsOutdoor { get; set; } = false;

    [Range(0, 10000)]
    public decimal PositionX { get; set; }

    [Range(0, 10000)]
    public decimal PositionY { get; set; }

    [Range(10, 500)]
    public decimal Width { get; set; } = 80;

    [Range(10, 500)]
    public decimal Height { get; set; } = 80;

    [MaxLength(20)]
    public string Shape { get; set; } = "circle";

    [Range(0, 360)]
    public int Rotation { get; set; } = 0;

    [MaxLength(500)]
    public string? Notes { get; set; }
}
