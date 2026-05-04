using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Features.FidelityPoints.Dtos;

public class AdjustPointsDto
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public int Points { get; set; } // Can be positive (add) or negative (deduct)

    [Required]
    public string Reason { get; set; } = string.Empty;
}
